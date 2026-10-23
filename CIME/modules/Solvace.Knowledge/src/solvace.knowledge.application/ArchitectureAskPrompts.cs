using System.Text;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Filtering;
using solvace.knowledge.domain.Responses;

namespace solvace.knowledge.application;

/// <summary>Uma fonte numerada do prompt do Pergunte (item da engenharia reversa, trecho de seção ou artigo do KC).</summary>
public sealed record AskSource(string Label, string Text);

/// <summary>Resposta da IA já lida: texto em markdown, cobertura, fontes usadas (números, da mais útil) e a seção principal.</summary>
public sealed record AskAnswer(string? Text, string Coverage, List<int> Sources, int Section);

/// <summary>
/// Prompts do "Pergunte à Base Solvace" (0071). A resposta é para quem USA o sistema (QA, suporte, gestor), não para quem
/// programa: resumo, quem pode, antes de começar, passo a passo na tela, o que acontece depois, cuidados e o que não está
/// confirmado — sem endpoint, tabela ou ID de item no texto. O formato é em bloco (<c>&lt;&lt;&lt;RESPOSTA</c>) porque markdown
/// longo dentro de JSON o modelo erra o escape.
/// </summary>
public static class ArchitectureAskPrompts
{
    public const string Tag = "RESPOSTA";

    public static string Plan(string question, string catalog) =>
        new StringBuilder()
            .AppendLine("Você ajuda a encontrar documentação na Base Solvace (engenharia reversa dos sistemas Solvace: legado edv-solvace .NET/ASP clássico, apps Angular, API de integrações, módulos revamp .NET, AWS, login/Cognito; e regras de negócio do Knowledge Center).")
            .AppendLine("Leia a PERGUNTA e o CATÁLOGO e devolva SÓ um JSON, sem texto fora dele:")
            .AppendLine("{\"kind\": \"operacao (como fazer/habilitar/configurar/dar acesso/cadastrar/onde fica/quem pode/por que não aparece) | regra (como o produto deve se comportar) | tecnica (implementação, código, banco) | outra\", \"terms\": [até 12 termos de busca curtos — como o USUÁRIO chama a coisa na tela e sinônimos em português e inglês (ex.: planta = unidade = site), mais nomes técnicos prováveis de tabelas, classes e telas], \"projects\": [chaves de até 4 projetos do catálogo mais prováveis]}")
            .AppendLine()
            .AppendLine("PERGUNTA: " + question)
            .AppendLine()
            .AppendLine("CATÁLOGO (chave | nome — seções):")
            .AppendLine(catalog)
            .ToString();

    public static string Answer(string question, string intent, IReadOnlyList<AskSource> sources, int maxSources)
    {
        var sb = new StringBuilder()
            .AppendLine("Você é o especialista da Base Solvace e responde para quem USA o sistema no dia a dia (QA, suporte, gestor, consultor) — pessoas que não programam.")
            .AppendLine("Use SÓ as FONTES numeradas abaixo; não invente nada que elas não digam. Se duas fontes divergem, prefira a mais detalhada e avise.")
            .AppendLine();
        if (intent == ArchitectureQuestionKind.Operation)
            sb.AppendLine("A pergunta é de COMO FAZER. Escreva em português simples, em markdown, com estes títulos em negrito (omita o título cujo conteúdo as fontes não trazem — sem escrever que \"as fontes não informam\"):")
                .AppendLine("**Resumo** — 1 ou 2 frases com a resposta direta.")
                .AppendLine("**Quem pode** — o perfil/permissão necessário.")
                .AppendLine("**Antes de começar** — o que precisa estar certo (estado do usuário/registro, dados, pré-condições).")
                .AppendLine("**Passo a passo** — lista numerada na ordem da tela: onde clicar, o que escolher ou preencher, o que confirmar, com os nomes de botões, menus e campos como aparecem na tela.")
                .AppendLine("**O que acontece depois** — efeitos para o usuário e para outras áreas.")
                .AppendLine("**Cuidados** — limites, mensagens de erro e armadilhas.")
                .AppendLine("**Se não funcionar** — causas comuns que as fontes citam (só se houver).")
                .AppendLine("**Não confirmado** — o que a base não confirma e vale checar com o suporte (só se for importante).");
        else if (intent == ArchitectureQuestionKind.Technical)
            sb.AppendLine("A pergunta é TÉCNICA: responda direto e organizado em markdown (tópicos curtos), pode citar nomes de tabelas, endpoints, classes e arquivos como estão nas fontes. Termine com **Não confirmado** se algo importante não está nas fontes.");
        else
            sb.AppendLine("Responda direto, em português simples e em markdown (frases curtas ou lista curta, até uns 8 itens). Use **Quem pode**, **Como funciona** e **Cuidados** só se ajudarem; omita o que as fontes não trazem.");
        sb.AppendLine()
            .AppendLine("Regras do texto:")
            .AppendLine("- Escreva para um leigo: troque termos técnicos por linguagem de negócio (planta = unidade/site). Nada de endpoint, tabela, classe, arquivo ou código de item (RN-…, UC-…) no texto" + (intent == ArchitectureQuestionKind.Technical ? " — exceto nesta pergunta técnica." : "."))
            .AppendLine("- Seja completo no que as fontes dizem e curto no resto: no máximo 250 palavras, frases curtas, passos de uma linha.")
            .AppendLine("- Se as fontes não respondem, diga isso em uma frase e use cobertura not-found.")
            .AppendLine()
            .AppendLine("Devolva SÓ este bloco, sem texto fora dele:")
            .AppendLine($"<<<{Tag}")
            .AppendLine("cobertura: answered (as fontes respondem) | partial (respondem em parte) | not-found (não respondem)")
            .AppendLine($"fontes: números das fontes que você usou, da mais útil para a menos útil (até {maxSources}), separados por vírgula")
            .AppendLine("secao: número da fonte cuja seção é a melhor para ler sobre o assunto (ou 0)")
            .AppendLine("---")
            .AppendLine("(a resposta em markdown)")
            .AppendLine($"{Tag}>>>")
            .AppendLine()
            .AppendLine("PERGUNTA: " + question)
            .AppendLine()
            .AppendLine("FONTES:");
        for (var i = 0; i < sources.Count; i++)
        {
            sb.AppendLine($"[{i + 1}] {sources[i].Label}");
            sb.AppendLine("    " + sources[i].Text.Replace("\n", "\n    "));
        }
        return sb.ToString();
    }

    /// <summary>Lê o bloco da resposta; null = o modelo não seguiu o formato (quem chama cai na busca sem IA).</summary>
    public static AskAnswer? ParseAnswer(string? content)
    {
        var block = ArchitectureBlocks.First(content, Tag);
        if (block is null)
        {
            // o modelo às vezes esquece de fechar o bloco: aproveita o texto depois do "---"
            var text = content ?? string.Empty;
            var open = text.IndexOf($"<<<{Tag}", StringComparison.Ordinal);
            if (open < 0) return null;
            var separator = text.IndexOf("\n---", open, StringComparison.Ordinal);
            if (separator < 0) return null;
            var head = text[open..separator];
            var body = text[(separator + 4)..].Replace($"{Tag}>>>", string.Empty).Trim();
            return body.Length == 0 ? null : new AskAnswer(body, ArchitectureCoverage.Normalize(Field(head, "cobertura")), Numbers(Field(head, "fontes")), Number(Field(head, "secao")));
        }
        return new AskAnswer(block.Body.Length > 0 ? block.Body : null, ArchitectureCoverage.Normalize(block.Field("cobertura")),
            Numbers(block.Field("fontes")), Number(block.Field("secao")));
    }

    private static string? Field(string head, string name)
    {
        foreach (var line in head.Split('\n'))
        {
            var i = line.IndexOf(':');
            if (i > 0 && line[..i].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return line[(i + 1)..].Trim();
        }
        return null;
    }

    private static List<int> Numbers(string? value) =>
        (value ?? string.Empty).Split([',', ';', ' ', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();

    private static int Number(string? value) => int.TryParse((value ?? string.Empty).Trim('[', ']', ' '), out var n) ? n : 0;
}
