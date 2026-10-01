using Cime.ExecutionAgent;

// Executor do PRMake (0039). Comandos:
//   register [--name N] [--workspace DIR] [--api URL]   registra esta máquina (usa a api-key das skills)
//   install | uninstall | status                        liga/desliga o serviço do usuário (LaunchAgent, systemd, tarefa)
//   run                                                  o laço (o serviço chama este)
//   doctor                                               checa a máquina e manda o resultado para o PRMake
//   update                                               baixa a versão publicada pelo PRMake
//   guard                                                hook PreToolUse do Claude Code (uso interno)
//   version

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
string? Option(string name) =>
    Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

try
{
    switch (command)
    {
        case "version" or "--version" or "-v":
            Console.WriteLine(Agent.Version);
            return 0;

        case "guard":
            return Guard.Run();

        case "register":
        {
            var config = ConfigStore.Load();
            if (Option("--api") is { } api) config.ApiBase = api.TrimEnd('/');
            if (Option("--workspace") is { } ws)
            {
                if (!Directory.Exists(ws)) throw new InvalidOperationException($"workspace não existe: {ws}");
                config.Workspace = Path.GetFullPath(ws);
            }
            var userToken = ConfigStore.UserToken()
                            ?? throw new InvalidOperationException($"api-key não encontrada (PRMAKE_TOKEN ou {Paths.UserTokenFile}) — instale as skills pela tela Skills do PRMake");
            using var client = new PrmakeClient(ConfigStore.ResolveApiBase(config), userToken);
            var registration = await client.RegisterAsync(new RegisterRequest
            {
                Host = Paths.HostName,
                Name = Option("--name") ?? config.Name,
                Os = Paths.OsDescription,
                AgentVersion = Agent.Version
            }, CancellationToken.None);
            config.Token = registration.Token;
            config.WorkerId = registration.Worker.Id;
            config.Name = registration.Worker.Name;
            ConfigStore.Save(config);
            Console.WriteLine($"máquina registrada: {registration.Worker.Name} ({registration.Worker.Id})");
            Console.WriteLine($"workspace: {ConfigStore.ResolveWorkspace(config)}");
            Console.WriteLine("próximo passo: prmake-agent install");
            return 0;
        }

        case "install":
            if (ConfigStore.Load().Token is null)
                throw new InvalidOperationException("registre a máquina antes: prmake-agent register");
            return await Service.InstallAsync();

        case "uninstall":
            return await Service.UninstallAsync();

        case "status":
        {
            var config = ConfigStore.Load();
            Console.WriteLine($"executor {Agent.Version} · serviço: {await Service.StatusAsync()}");
            Console.WriteLine($"config: {Paths.Config} · log: {Paths.Log}");
            Console.WriteLine($"workspace: {ConfigStore.ResolveWorkspace(config)}");
            if (config.Token is null)
            {
                Console.WriteLine("não registrado — rode: prmake-agent register");
                return 1;
            }
            using var client = new PrmakeClient(ConfigStore.ResolveApiBase(config), config.Token);
            var me = await client.MeAsync(CancellationToken.None);
            Console.WriteLine($"máquina: {me.Name} · {(me.Online ? "online" : "offline")} · {me.Status} · rodando {me.Running}/{me.MaxConcurrency}" +
                              (me.DoctorProblems > 0 ? $" · {me.DoctorProblems} problema(s) no doctor" : ""));
            if (me.LatestAgentVersion is { } latest && Updater.IsNewer(latest, Agent.Version))
                Console.WriteLine($"versão nova disponível: {latest} (prmake-agent update)");
            return 0;
        }

        case "doctor":
        {
            var config = ConfigStore.Load();
            using var client = config.Token is null ? null : new PrmakeClient(ConfigStore.ResolveApiBase(config), config.Token);
            var checks = await Doctor.RunChecksAsync(config, client, CancellationToken.None);
            foreach (var c in checks)
                Console.WriteLine($"{(c.Ok ? "ok  " : c.Severity == "warning" ? "aviso" : "ERRO")}  {c.Name}: {c.Message}");
            if (client is not null)
                await client.DoctorAsync(new DoctorRequest { Checks = checks }, CancellationToken.None);
            return checks.Any(c => !c.Ok && c.Severity != "warning") ? 1 : 0;
        }

        case "update":
        {
            var config = ConfigStore.Load();
            var token = config.Token ?? ConfigStore.UserToken() ?? throw new InvalidOperationException("sem credencial para baixar");
            using var client = new PrmakeClient(ConfigStore.ResolveApiBase(config), token);
            var descriptor = await client.AgentDescriptorAsync(CancellationToken.None);
            if (descriptor?.Version is not { } latest || !Updater.IsNewer(latest, Agent.Version))
            {
                Console.WriteLine($"já está na versão publicada ({Agent.Version})");
                return 0;
            }
            if (!await Updater.UpdateAsync(client, CancellationToken.None)) return 1;
            await Service.RestartAsync();
            return 0;
        }

        case "run":
        {
            var config = ConfigStore.Load();
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
            // Depois de uma atualização no Windows: espera a instância anterior soltar o lock.
            if (args.Contains("--wait-lock"))
                for (var i = 0; i < 30; i++)
                {
                    using (var probe = SingleInstance.TryAcquire())
                        if (probe is not null) break;
                    await Task.Delay(2000);
                }
            return await new Runner(config).RunAsync(stop.Token);
        }

        default:
            Console.WriteLine("""
                prmake-agent — executor do PRMake (roda a skill analisar-bug sem terminal)

                  register [--name N] [--workspace DIR]   registra esta máquina (usa a api-key das skills)
                  install | uninstall | status            liga/desliga o serviço do usuário
                  doctor                                  checa Claude Code, skills, api-key, repositórios e bancos
                  update                                  baixa a versão publicada pelo PRMake
                  run                                     o laço (o serviço chama este)
                """);
            return command == "help" ? 0 : 1;
    }
}
catch (Exception e)
{
    Console.Error.WriteLine($"ERRO: {e.Message}");
    return 1;
}
