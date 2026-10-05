#!/usr/bin/env python3
"""
re_infra.py — etapa OPCIONAL de infraestrutura da engenharia reversa (0058).

Mapeia, pelo AWS CLI e SOMENTE COM CHAMADAS DE LEITURA (list/describe/get — nunca get-secret-value, nunca
receive-message, nunca nada que grave), tudo o que a conta tem: Lambdas, buckets S3, esteiras (CodePipeline, CodeBuild,
CodeDeploy), segredos (so nomes e datas — nunca o valor), grupos de log do CloudWatch, filas, topicos, regras do
EventBridge, bancos, Cognito, CloudFront, DNS, alarmes etc. Depois liga os recursos ao modulo (nome, sigla, o que o
codigo cita, relacoes de um salto) e le as esteiras que vivem nos repositorios (GitHub Actions, buildspec, Dockerfile,
serverless.template, appspec).

Saida em <pasta>/ (por padrao ~/.prmake/reverse/<modulo>/infra/):
  conta-<id>.json        tudo o que a conta tem (referencia; NAO entra na cobertura)
  resumo.md              contagem e nomes por servico + o que nao deu para ler (sem permissao)
  modulo.md              os recursos ligados ao modulo, com logs (grupo + comando) e esteira
  ../inventario-infra.json   so os recursos ligados ao modulo + esteiras do repositorio (a cobertura exige estes)

Nao grava segredo: variaveis de ambiente so por NOME (valor so de chaves de referencia a recurso, ex. QUEUE_NAME),
buildspec com valores mascarados, e-mail de assinatura mascarado, endpoints de banco so no arquivo local da conta.
"""
import argparse
import concurrent.futures as cf
import json
import os
import re
import subprocess
import sys
from collections import defaultdict

TIMEOUT = 120
DENIED = re.compile(r"AccessDenied|UnauthorizedOperation|not authorized|AuthorizationError|is not subscribed|OptInRequired|"
                    r"UnrecognizedClientException|InvalidClientTokenId|ExpiredToken", re.I)
SENSITIVE_KEY = re.compile(r"PASS|SECRET|TOKEN|KEY|CRED|CONNECTION|CONN_?STR|AUTH|PWD|PRIVATE|CERT", re.I)
REF_KEY = re.compile(r"QUEUE|TOPIC|BUCKET|TABLE|STREAM|SNS|SQS|S3|LAMBDA|FUNCTION|REGION|STAGE|ENVIRONMENT|^ENV$|LOG_?GROUP|CLUSTER|PIPELINE|SECRET_?(NAME|ID)", re.I)
SECRET_LINE = re.compile(r"(?i)((?:pass(?:word)?|secret|token|api[_-]?key|access[_-]?key|private[_-]?key|pwd)\s*[:=]\s*)(\S+)")
EMAIL = re.compile(r"[\w.+-]+@([\w-]+\.[\w.-]+)")


class Aws:
    """Chamadas de leitura. Qualquer verbo fora da lista branca e recusado antes de chegar ao CLI."""

    SAFE = re.compile(r"^(list|describe|get|batch-get|lookup|search|scan-?)", re.I)
    FORBIDDEN = re.compile(r"get-secret-value|get-parameter(s)?(-by-path)?$|receive-message|get-object$|get-authorization-token|"
                           r"decrypt|get-session-token|get-credentials|generate-", re.I)

    def __init__(self, profile, region):
        self.profile, self.region = profile, region
        self.denied = []   # (servico/comando, mensagem)
        self.failed = []

    def call(self, service, command, *args, regional=True, quiet=False):
        if not self.SAFE.match(command) or self.FORBIDDEN.search(command):
            raise RuntimeError(f"comando fora da lista de leitura: {service} {command}")
        cmd = ["aws", service, command, *args, "--output", "json", "--profile", self.profile]
        if regional:
            cmd += ["--region", self.region]
        try:
            p = subprocess.run(cmd, capture_output=True, text=True, timeout=TIMEOUT)
        except subprocess.TimeoutExpired:
            self.failed.append((f"{service} {command}", "tempo esgotado"))
            return None
        if p.returncode != 0:
            if re.search(r"NoSuch|NotFound|does not exist|ServerSideEncryptionConfigurationNotFound|PermanentRedirect", p.stderr or ""):
                return None  # recurso sem a configuracao pedida (ex. bucket sem website): nao e problema
            msg = (p.stderr or "").strip().splitlines()[-1:] or [""]
            (self.denied if DENIED.search(p.stderr or "") else self.failed).append((f"{service} {command}", msg[0][:200]))
            return None
        try:
            return json.loads(p.stdout) if p.stdout.strip() else {}
        except json.JSONDecodeError:
            return None


def short(arn):
    return (arn or "").split(":")[-1].split("/")[-1]


def mask_text(text):
    return SECRET_LINE.sub(lambda m: m.group(1) + "***", text or "")


def pmap(fn, items, workers=8):
    with cf.ThreadPoolExecutor(max_workers=workers) as ex:
        return list(ex.map(fn, items))


def iso(v):
    return str(v)[:19] if v else ""


# ── coletores (um por servico; devolvem lista de recursos {svc,name,arn,...}) ─────────────────────────────────────
def c_lambda(a):
    out = []
    data = a.call("lambda", "list-functions") or {}
    mappings = defaultdict(list)
    for m in (a.call("lambda", "list-event-source-mappings") or {}).get("EventSourceMappings", []):
        mappings[m.get("FunctionArn", "")].append({"source": m.get("EventSourceArn", ""), "state": m.get("State", ""), "batch": m.get("BatchSize")})
    for f in data.get("Functions", []):
        env = (f.get("Environment") or {}).get("Variables") or {}
        refs = {k: v for k, v in env.items() if REF_KEY.search(k) and not SENSITIVE_KEY.search(k) and len(str(v)) <= 100
                and not re.search(r"://[^/]*:[^/]*@", str(v))}
        out.append({"svc": "lambda", "name": f["FunctionName"], "arn": f["FunctionArn"], "runtime": f.get("Runtime", f.get("PackageType", "")),
                    "handler": f.get("Handler", ""), "memory": f.get("MemorySize"), "timeout": f.get("Timeout"),
                    "role": short(f.get("Role")), "modified": iso(f.get("LastModified")), "arch": ",".join(f.get("Architectures", [])),
                    "envNames": sorted(env), "envRefs": refs, "vpc": bool((f.get("VpcConfig") or {}).get("SubnetIds")),
                    "triggers": mappings.get(f["FunctionArn"], []),
                    "logGroup": (f.get("LoggingConfig") or {}).get("LogGroup") or f"/aws/lambda/{f['FunctionName']}",
                    "description": (f.get("Description") or "")[:200]})
    return out


def c_s3(a):
    if a.region != a.primary_region:
        return []
    data = a.call("s3api", "list-buckets", regional=False) or {}

    def one(b):
        n = b["Name"]
        loc = (a.call("s3api", "get-bucket-location", "--bucket", n, regional=False) or {}).get("LocationConstraint") or "us-east-1"
        ver = (a.call("s3api", "get-bucket-versioning", "--bucket", n, regional=False) or {}).get("Status", "")
        notif = a.call("s3api", "get-bucket-notification-configuration", "--bucket", n, regional=False) or {}
        targets = [{"type": t, "arn": x.get(k), "events": x.get("Events", [])}
                   for t, k, key in (("lambda", "LambdaFunctionArn", "LambdaFunctionConfigurations"), ("sqs", "QueueArn", "QueueConfigurations"),
                                     ("sns", "TopicArn", "TopicConfigurations")) for x in notif.get(key, [])]
        website = bool(a.call("s3api", "get-bucket-website", "--bucket", n, regional=False))
        return {"svc": "s3", "name": n, "arn": f"arn:aws:s3:::{n}", "bucketRegion": loc, "created": iso(b.get("CreationDate")),
                "versioning": ver, "notifications": targets, "website": website}
    return pmap(one, data.get("Buckets", []))


def c_codebuild(a):
    names = (a.call("codebuild", "list-projects") or {}).get("projects", [])
    out = []
    for i in range(0, len(names), 50):
        for p in (a.call("codebuild", "batch-get-projects", "--names", *names[i:i + 50]) or {}).get("projects", []):
            src, env = p.get("source") or {}, p.get("environment") or {}
            bs = src.get("buildspec") or ""
            out.append({"svc": "codebuild", "name": p["name"], "arn": p["arn"], "sourceType": src.get("type", ""), "sourceLocation": src.get("location", ""),
                        "buildspecInline": mask_text(bs)[:4000] if bs and "\n" in bs else "", "buildspecFile": bs if bs and "\n" not in bs else "",
                        "image": env.get("image", ""), "compute": env.get("computeType", ""), "privileged": env.get("privilegedMode", False),
                        "envNames": sorted(e["name"] for e in env.get("environmentVariables", [])),
                        "envFromStore": {e["name"]: f"{e['type']}:{e['value']}" for e in env.get("environmentVariables", [])
                                         if e.get("type") in ("PARAMETER_STORE", "SECRETS_MANAGER")},
                        "role": short(p.get("serviceRole")), "modified": iso(p.get("lastModified")),
                        "logGroup": ((p.get("logsConfig") or {}).get("cloudWatchLogs") or {}).get("groupName") or f"/aws/codebuild/{p['name']}",
                        "webhook": bool(p.get("webhook")), "artifacts": (p.get("artifacts") or {}).get("type", "")})
    return out


def c_codepipeline(a):
    names = [p["name"] for p in (a.call("codepipeline", "list-pipelines") or {}).get("pipelines", [])]

    def one(n):
        pl = (a.call("codepipeline", "get-pipeline", "--name", n) or {}).get("pipeline") or {}
        stages = []
        for s in pl.get("stages", []):
            acts = []
            for x in s.get("actions", []):
                cfg = x.get("configuration") or {}
                keep = {k: v for k, v in cfg.items() if k in (
                    "FullRepositoryId", "BranchName", "ProjectName", "ApplicationName", "DeploymentGroupName", "BucketName", "S3Bucket", "S3ObjectKey",
                    "FunctionName", "StackName", "TemplatePath", "Repo", "Owner", "Branch", "ClusterName", "ServiceName", "ConnectionArn",
                    "PollForSourceChanges", "DetectChanges", "ExternalEntityLink", "CustomData", "NotificationArn")}
                acts.append({"name": x["name"], "category": x["actionTypeId"]["category"], "provider": x["actionTypeId"]["provider"], "config": keep})
            stages.append({"name": s["name"], "actions": acts})
        last = (a.call("codepipeline", "list-pipeline-executions", "--pipeline-name", n, "--max-items", "1") or {}).get("pipelineExecutionSummaries", [])
        return {"svc": "codepipeline", "name": n, "arn": f"arn:aws:codepipeline:{a.region}:{a.account}:{n}", "type": pl.get("pipelineType", ""),
                "role": short(pl.get("roleArn")), "artifactStore": (pl.get("artifactStore") or {}).get("location", ""), "stages": stages,
                "lastExecution": {"status": last[0].get("status"), "at": iso(last[0].get("lastUpdateTime")),
                                  "trigger": (last[0].get("trigger") or {}).get("triggerType", "")} if last else None}
    return pmap(one, names)


def c_codedeploy(a):
    out = []
    for app in (a.call("deploy", "list-applications") or {}).get("applications", []):
        groups = (a.call("deploy", "list-deployment-groups", "--application-name", app) or {}).get("deploymentGroups", [])
        out.append({"svc": "codedeploy", "name": app, "arn": f"arn:aws:codedeploy:{a.region}:{a.account}:application:{app}", "deploymentGroups": groups})
    return out


def c_secrets(a):
    """So metadados. Nunca o valor: nao existe chamada a get-secret-value neste arquivo."""
    out = []
    for s in (a.call("secretsmanager", "list-secrets") or {}).get("SecretList", []):
        out.append({"svc": "secret", "name": s["Name"], "arn": s["ARN"], "description": (s.get("Description") or "")[:200],
                    "changed": iso(s.get("LastChangedDate")), "accessed": iso(s.get("LastAccessedDate")), "rotation": bool(s.get("RotationEnabled")),
                    "tagKeys": sorted(t["Key"] for t in s.get("Tags", [])), "replica": bool(s.get("PrimaryRegion"))})
    return out


def c_ssm(a):
    return [{"svc": "ssm", "name": p["Name"], "arn": p.get("ARN", p["Name"]), "type": p.get("Type", ""), "changed": iso(p.get("LastModifiedDate"))}
            for p in (a.call("ssm", "describe-parameters") or {}).get("Parameters", [])]


def c_logs(a):
    return [{"svc": "log-group", "name": g["logGroupName"], "arn": g.get("arn", g["logGroupName"]).rstrip(":*"), "retention": g.get("retentionInDays", "nunca expira"),
             "storedMB": round(g.get("storedBytes", 0) / 1e6, 1), "metricFilters": g.get("metricFilterCount", 0), "created": iso(g.get("creationTime") and g["creationTime"] // 1000)}
            for g in (a.call("logs", "describe-log-groups") or {}).get("logGroups", [])]


def c_sqs(a):
    urls = (a.call("sqs", "list-queues") or {}).get("QueueUrls", [])

    def one(u):
        at = (a.call("sqs", "get-queue-attributes", "--queue-url", u, "--attribute-names", "All") or {}).get("Attributes", {})
        redrive = json.loads(at["RedrivePolicy"]) if at.get("RedrivePolicy") else {}
        return {"svc": "sqs", "name": u.rsplit("/", 1)[-1], "arn": at.get("QueueArn", ""), "fifo": at.get("FifoQueue") == "true",
                "visibility": at.get("VisibilityTimeout"), "retentionDays": round(int(at.get("MessageRetentionPeriod", 0)) / 86400, 1),
                "deadLetter": short(redrive.get("deadLetterTargetArn")), "maxReceive": redrive.get("maxReceiveCount"),
                "messages": at.get("ApproximateNumberOfMessages"), "inFlight": at.get("ApproximateNumberOfMessagesNotVisible")}
    return pmap(one, urls)


def c_sns(a):
    subs = defaultdict(list)
    for s in (a.call("sns", "list-subscriptions") or {}).get("Subscriptions", []):
        ep = s.get("Endpoint", "")
        subs[s["TopicArn"]].append({"protocol": s.get("Protocol"), "endpoint": EMAIL.sub(lambda m: "***@" + m.group(1), ep) if s.get("Protocol") == "email" else ep})
    return [{"svc": "sns", "name": short(t["TopicArn"]), "arn": t["TopicArn"], "subscriptions": subs.get(t["TopicArn"], [])}
            for t in (a.call("sns", "list-topics") or {}).get("Topics", [])]


def c_events(a):
    out = []
    for bus in (a.call("events", "list-event-buses") or {}).get("EventBuses", []):
        rules = (a.call("events", "list-rules", "--event-bus-name", bus["Name"]) or {}).get("Rules", [])

        def one(r, bus=bus):
            tg = (a.call("events", "list-targets-by-rule", "--rule", r["Name"], "--event-bus-name", bus["Name"]) or {}).get("Targets", [])
            return {"svc": "events-rule", "name": r["Name"], "arn": r["Arn"], "bus": bus["Name"], "schedule": r.get("ScheduleExpression", ""),
                    "pattern": (r.get("EventPattern") or "")[:300], "state": r.get("State", ""), "description": (r.get("Description") or "")[:200],
                    "targets": [{"arn": t["Arn"], "input": bool(t.get("Input") or t.get("InputPath") or t.get("InputTransformer"))} for t in tg]}
        out += pmap(one, rules)
    return out


def c_scheduler(a):
    out = []
    for s in (a.call("scheduler", "list-schedules") or {}).get("Schedules", []):
        out.append({"svc": "scheduler", "name": s["Name"], "arn": s["Arn"], "group": s.get("GroupName", ""), "state": s.get("State", ""),
                    "target": short((s.get("Target") or {}).get("Arn"))})
    return out


def c_rds(a):
    out = []
    for d in (a.call("rds", "describe-db-instances") or {}).get("DBInstances", []):
        out.append({"svc": "rds", "name": d["DBInstanceIdentifier"], "arn": d["DBInstanceArn"], "engine": f"{d.get('Engine')} {d.get('EngineVersion')}",
                    "class": d.get("DBInstanceClass"), "multiAz": d.get("MultiAZ"), "storageGB": d.get("AllocatedStorage"),
                    "cluster": d.get("DBClusterIdentifier", ""), "public": d.get("PubliclyAccessible"),
                    "endpoint": (d.get("Endpoint") or {}).get("Address", ""), "dbName": d.get("DBName", "")})
    for c in (a.call("rds", "describe-db-clusters") or {}).get("DBClusters", []):
        out.append({"svc": "rds-cluster", "name": c["DBClusterIdentifier"], "arn": c["DBClusterArn"], "engine": f"{c.get('Engine')} {c.get('EngineVersion')}",
                    "members": [m["DBInstanceIdentifier"] for m in c.get("DBClusterMembers", [])], "endpoint": c.get("Endpoint", ""),
                    "serverless": bool(c.get("ServerlessV2ScalingConfiguration"))})
    return out


def c_cognito(a):
    out = []
    for p in (a.call("cognito-idp", "list-user-pools", "--max-results", "60") or {}).get("UserPools", []):
        cl = (a.call("cognito-idp", "list-user-pool-clients", "--user-pool-id", p["Id"], "--max-results", "60") or {}).get("UserPoolClients", [])
        out.append({"svc": "cognito", "name": p["Name"], "arn": f"arn:aws:cognito-idp:{a.region}:{a.account}:userpool/{p['Id']}", "id": p["Id"],
                    "clients": [c["ClientName"] for c in cl], "triggers": sorted((p.get("LambdaConfig") or {}).keys())})
    return out


def c_cloudfront(a):
    if a.region != a.primary_region:
        return []
    d = ((a.call("cloudfront", "list-distributions", regional=False) or {}).get("DistributionList") or {}).get("Items", [])
    return [{"svc": "cloudfront", "name": x["Id"], "arn": x["ARN"], "aliases": (x.get("Aliases") or {}).get("Items", []),
             "origins": [o["DomainName"] for o in (x.get("Origins") or {}).get("Items", [])], "comment": (x.get("Comment") or "")[:150],
             "status": x.get("Status", "")} for x in d]


def c_route53(a):
    if a.region != a.primary_region:
        return []
    out = []
    for z in (a.call("route53", "list-hosted-zones", regional=False) or {}).get("HostedZones", []):
        zid = z["Id"].split("/")[-1]
        recs = (a.call("route53", "list-resource-record-sets", "--hosted-zone-id", zid, regional=False) or {}).get("ResourceRecordSets", [])
        out.append({"svc": "route53", "name": z["Name"].rstrip("."), "arn": z["Id"], "private": (z.get("Config") or {}).get("PrivateZone", False),
                    "records": [f"{r['Type']} {r['Name'].rstrip('.')}" for r in recs if r["Type"] in ("A", "AAAA", "CNAME")][:200]})
    return out


def c_acm(a):
    return [{"svc": "acm", "name": c["DomainName"], "arn": c["CertificateArn"], "status": c.get("Status", ""), "inUse": bool(c.get("InUse")),
             "names": c.get("SubjectAlternativeNameSummaries", [])[:10]} for c in (a.call("acm", "list-certificates") or {}).get("CertificateSummaryList", [])]


def c_apigw(a):
    out = []
    for x in (a.call("apigateway", "get-rest-apis") or {}).get("items", []):
        out.append({"svc": "apigateway", "name": x["name"], "arn": f"arn:aws:apigateway:{a.region}::/restapis/{x['id']}", "id": x["id"], "kind": "REST"})
    for x in (a.call("apigatewayv2", "get-apis") or {}).get("Items", []):
        out.append({"svc": "apigateway", "name": x["Name"], "arn": f"arn:aws:apigateway:{a.region}::/apis/{x['ApiId']}", "id": x["ApiId"], "kind": x.get("ProtocolType", "HTTP")})
    return out


def c_dynamodb(a):
    return [{"svc": "dynamodb", "name": n, "arn": f"arn:aws:dynamodb:{a.region}:{a.account}:table/{n}"} for n in (a.call("dynamodb", "list-tables") or {}).get("TableNames", [])]


def c_elasticache(a):
    out = [{"svc": "elasticache", "name": r["ReplicationGroupId"], "arn": r.get("ARN", r["ReplicationGroupId"]), "engine": "redis/valkey",
            "nodes": len(r.get("MemberClusters", [])), "description": (r.get("Description") or "")[:150]}
           for r in (a.call("elasticache", "describe-replication-groups") or {}).get("ReplicationGroups", [])]
    out += [{"svc": "elasticache", "name": c["CacheClusterId"], "arn": c.get("ARN", c["CacheClusterId"]), "engine": f"{c.get('Engine')} {c.get('EngineVersion')}",
             "nodeType": c.get("CacheNodeType")} for c in (a.call("elasticache", "describe-cache-clusters") or {}).get("CacheClusters", []) if not c.get("ReplicationGroupId")]
    return out


def c_alarms(a):
    return [{"svc": "alarm", "name": x["AlarmName"], "arn": x["AlarmArn"], "metric": f"{x.get('Namespace', '')}/{x.get('MetricName', '')}", "state": x.get("StateValue", ""),
             "actions": [short(t) for t in x.get("AlarmActions", [])]} for x in (a.call("cloudwatch", "describe-alarms") or {}).get("MetricAlarms", [])]


def c_stepfunctions(a):
    return [{"svc": "stepfunctions", "name": s["name"], "arn": s["stateMachineArn"], "type": s.get("type", "")}
            for s in (a.call("stepfunctions", "list-state-machines") or {}).get("stateMachines", [])]


def c_ecs(a):
    out = []
    for arn in (a.call("ecs", "list-clusters") or {}).get("clusterArns", []):
        svcs = (a.call("ecs", "list-services", "--cluster", arn) or {}).get("serviceArns", [])
        out.append({"svc": "ecs", "name": short(arn), "arn": arn, "services": [short(s) for s in svcs]})
    return out


def c_ecr(a):
    return [{"svc": "ecr", "name": r["repositoryName"], "arn": r["repositoryArn"], "uri": r["repositoryUri"]}
            for r in (a.call("ecr", "describe-repositories") or {}).get("repositories", [])]


def c_ec2(a):
    out = []
    for r in (a.call("ec2", "describe-instances") or {}).get("Reservations", []):
        for i in r.get("Instances", []):
            nm = next((t["Value"] for t in i.get("Tags", []) if t["Key"] == "Name"), i["InstanceId"])
            out.append({"svc": "ec2", "name": nm, "arn": i["InstanceId"], "type": i.get("InstanceType"), "state": (i.get("State") or {}).get("Name")})
    return out


def c_beanstalk(a):
    return [{"svc": "beanstalk", "name": e["EnvironmentName"], "arn": e.get("EnvironmentArn", e["EnvironmentName"]), "application": e.get("ApplicationName"),
             "platform": e.get("SolutionStackName") or e.get("PlatformArn", ""), "status": e.get("Status"), "url": e.get("CNAME", "")}
            for e in (a.call("elasticbeanstalk", "describe-environments") or {}).get("Environments", [])]


def c_elbv2(a):
    return [{"svc": "load-balancer", "name": b["LoadBalancerName"], "arn": b["LoadBalancerArn"], "type": b.get("Type"), "scheme": b.get("Scheme"), "dns": b.get("DNSName")}
            for b in (a.call("elbv2", "describe-load-balancers") or {}).get("LoadBalancers", [])]


def c_kms(a):
    return [{"svc": "kms-alias", "name": x["AliasName"], "arn": x["AliasArn"]} for x in (a.call("kms", "list-aliases") or {}).get("Aliases", [])
            if not x["AliasName"].startswith("alias/aws/")]


def c_codeartifact(a):
    return [{"svc": "codeartifact", "name": r["name"], "arn": r["arn"], "domain": r.get("domainName"), "description": (r.get("description") or "")[:150]}
            for r in (a.call("codeartifact", "list-repositories") or {}).get("repositories", [])]


def c_glue(a):
    out = [{"svc": "glue-database", "name": d["Name"], "arn": f"arn:aws:glue:{a.region}:{a.account}:database/{d['Name']}"} for d in (a.call("glue", "get-databases") or {}).get("DatabaseList", [])]
    out += [{"svc": "glue-job", "name": j, "arn": f"arn:aws:glue:{a.region}:{a.account}:job/{j}"} for j in (a.call("glue", "list-jobs") or {}).get("JobNames", [])]
    out += [{"svc": "glue-crawler", "name": c, "arn": f"arn:aws:glue:{a.region}:{a.account}:crawler/{c}"} for c in (a.call("glue", "list-crawlers") or {}).get("CrawlerNames", [])]
    return out


def c_ses(a):
    return [{"svc": "ses", "name": i["IdentityName"], "arn": i["IdentityName"], "type": i.get("IdentityType", ""), "sending": i.get("SendingEnabled")}
            for i in (a.call("sesv2", "list-email-identities") or {}).get("EmailIdentities", [])]


def c_kinesis(a):
    return [{"svc": "kinesis", "name": n, "arn": f"arn:aws:kinesis:{a.region}:{a.account}:stream/{n}"} for n in (a.call("kinesis", "list-streams") or {}).get("StreamNames", [])]


COLLECTORS = [("lambda", c_lambda), ("s3", c_s3), ("codepipeline", c_codepipeline), ("codebuild", c_codebuild), ("codedeploy", c_codedeploy),
              ("secretsmanager", c_secrets), ("ssm", c_ssm), ("logs", c_logs), ("sqs", c_sqs), ("sns", c_sns), ("events", c_events),
              ("scheduler", c_scheduler), ("rds", c_rds), ("cognito", c_cognito), ("cloudfront", c_cloudfront), ("route53", c_route53),
              ("acm", c_acm), ("apigateway", c_apigw), ("dynamodb", c_dynamodb), ("elasticache", c_elasticache), ("cloudwatch-alarms", c_alarms),
              ("stepfunctions", c_stepfunctions), ("ecs", c_ecs), ("ecr", c_ecr), ("ec2", c_ec2), ("beanstalk", c_beanstalk), ("elbv2", c_elbv2),
              ("kms", c_kms), ("codeartifact", c_codeartifact), ("glue", c_glue), ("ses", c_ses), ("kinesis", c_kinesis)]

LABEL = {"lambda": "Lambda", "s3": "Bucket S3", "codepipeline": "Esteira CodePipeline", "codebuild": "Projeto CodeBuild", "codedeploy": "Aplicacao CodeDeploy",
         "secret": "Segredo (Secrets Manager)", "ssm": "Parametro SSM", "log-group": "Grupo de log (CloudWatch)", "sqs": "Fila SQS", "sns": "Topico SNS",
         "events-rule": "Regra EventBridge", "scheduler": "Agendamento (EventBridge Scheduler)", "rds": "Banco RDS", "rds-cluster": "Cluster RDS/Aurora",
         "cognito": "Cognito (user pool)", "cloudfront": "CloudFront", "route53": "DNS (Route 53)", "acm": "Certificado ACM", "apigateway": "API Gateway",
         "dynamodb": "Tabela DynamoDB", "elasticache": "ElastiCache", "alarm": "Alarme CloudWatch", "stepfunctions": "Step Functions", "ecs": "Cluster ECS",
         "ecr": "Repositorio ECR", "ec2": "Instancia EC2", "beanstalk": "Elastic Beanstalk", "load-balancer": "Load balancer", "kms-alias": "Chave KMS (alias)",
         "codeartifact": "Repositorio CodeArtifact", "glue-database": "Glue database", "glue-job": "Glue job", "glue-crawler": "Glue crawler",
         "ses": "Identidade SES", "kinesis": "Stream Kinesis"}


# ── liga os recursos ao modulo ─────────────────────────────────────────────────────────────────────────────────────
def norm(s):
    return re.sub(r"[^a-z0-9]+", "", (s or "").lower())


def terms_for(module, sigla, inventario, extra):
    generic = {"legado", "revamp", "api", "app", "web", "service", "services", "module", "modulo", "solvace", "edv"}
    ts = {t for t in re.split(r"[-_.\s]+", module.lower()) if len(t) >= 3 and t not in generic}
    if sigla:
        ts.add(sigla.lower())
    for t in extra:
        ts.add(t.lower())
    code = set()
    for it in (inventario or {}).get("items", []):
        if it["cat"] in ("evento", "job", "config", "handler") and 5 <= len(it["name"]) <= 80 and re.search(r"queue|topic|bucket|lambda|function|sqs|sns|s3|secret|stream|table|pipeline", it["name"], re.I):
            code.add(it["name"].lower())
    return ts, code


def link(resources, terms, code_names, own_arns):
    """Recursos que citam o modulo (nome/descricao) + os que o codigo cita + vizinhos de um salto."""
    by_arn = {r["arn"]: r for r in resources if r.get("arn")}
    by_name = defaultdict(list)
    for r in resources:
        by_name[r["name"]].append(r)
    hit, why = set(), {}

    def text(r):
        return norm(" ".join([r["name"], r.get("description", ""), r.get("comment", ""), " ".join(r.get("aliases", [])), r.get("application", "") or ""]))
    for i, r in enumerate(resources):
        t = text(r)
        for term in terms:
            if len(term) >= 3 and norm(term) in t:
                hit.add(i); why[i] = f"nome/descricao cita '{term}'"; break
        else:
            for cn in code_names:
                if len(cn) >= 5 and norm(cn) in t:
                    hit.add(i); why[i] = f"o codigo cita '{cn}'"; break
        if i not in hit and r["arn"] in own_arns:
            hit.add(i); why[i] = "informado pelo usuario (--recurso)"
    idx = {id(r): i for i, r in enumerate(resources)}
    # um salto: o que os recursos achados usam / quem os usa
    def neighbours(r):
        arns = set()
        for t in r.get("triggers", []):
            arns.add(t["source"])
        for t in r.get("notifications", []):
            arns.add(t["arn"])
        for t in r.get("targets", []):
            arns.add(t["arn"])
        for s in r.get("stages", []):
            for ac in s["actions"]:
                for k in ("ProjectName", "FunctionName", "ApplicationName"):
                    if ac["config"].get(k):
                        arns.add("name:" + ac["config"][k])
        if r.get("logGroup"):
            arns.add("name:" + r["logGroup"])
        if r.get("deadLetter"):
            arns.add("name:" + r["deadLetter"])
        if r.get("target"):
            arns.add("name:" + r["target"])
        if r.get("svc") == "lambda":
            arns.add("name:" + f"/aws/lambda/{r['name']}")
        return arns
    for i in list(hit):
        r = resources[i]
        for n in neighbours(r):
            targets = by_name.get(n[5:], []) if n.startswith("name:") else ([by_arn[n]] if n in by_arn else [])
            for o in targets:
                j = idx[id(o)]
                if j not in hit:
                    hit.add(j); why[j] = f"ligado a {r['svc']}:{r['name']}"
    # quem aponta para os achados (regra->lambda, bucket->lambda, mapping->lambda, pipeline->build)
    hit_arns = {resources[i]["arn"] for i in hit} | {"name:" + resources[i]["name"] for i in hit}
    for i, r in enumerate(resources):
        if i not in hit and neighbours(r) & hit_arns:
            hit.add(i); why[i] = "aponta para um recurso do modulo"
    return sorted(hit), why


# ── esteiras que vivem nos repositorios ────────────────────────────────────────────────────────────────────────────
PIPE_FILES = [(re.compile(r"(^|/)\.github/workflows/[^/]+\.ya?ml$"), "github-actions"), (re.compile(r"(^|/)buildspec[^/]*\.ya?ml$"), "buildspec"),
              (re.compile(r"(^|/)appspec\.ya?ml$"), "appspec"), (re.compile(r"(^|/)Dockerfile[^/]*$"), "dockerfile"),
              (re.compile(r"(^|/)(serverless\.template|template\.ya?ml|samconfig\.toml|serverless\.ya?ml|cdk\.json)$"), "iac"),
              (re.compile(r"(^|/)aws-lambda-tools-defaults\.json$"), "lambda-tools"), (re.compile(r"(^|/)azure-pipelines[^/]*\.ya?ml$"), "azure-pipelines"),
              (re.compile(r"(^|/)(Jenkinsfile|bitbucket-pipelines\.yml|\.gitlab-ci\.yml)$"), "ci")]
INTERESTING = re.compile(r"aws |s3://|--function-name|--bucket|ecr|codeartifact|deploy|publish|dotnet lambda|sam |cdk |terraform|cloudfront|secrets\.|arn:aws|"
                         r"role-to-assume|aws-region|FunctionName|QueueUrl|\bon:|branches|workflow_dispatch", re.I)


def scan_repo_pipelines(sources):
    items = []
    for _, path in sources:
        root = path if os.path.isdir(path) else os.path.dirname(path)
        top = subprocess.run(["git", "-C", root, "rev-parse", "--show-toplevel"], capture_output=True, text=True).stdout.strip() or root
        for base, dirs, files in os.walk(top):
            dirs[:] = [d for d in dirs if d not in ("node_modules", "bin", "obj", ".git", "dist", "packages", ".angular")]
            for fn in files:
                p = os.path.join(base, fn)
                rel = os.path.relpath(p, top).replace(os.sep, "/")
                kind = next((k for rx, k in PIPE_FILES if rx.search(rel)), None)
                if not kind:
                    continue
                try:
                    lines = open(p, encoding="utf-8", errors="replace").read().splitlines()
                except OSError:
                    continue
                facts, secrets = [], set()
                for n, ln in enumerate(lines, 1):
                    for m in re.finditer(r"secrets\.([A-Z0-9_]+)", ln):
                        secrets.add(m.group(1))
                    if INTERESTING.search(ln) and len(facts) < 40:
                        facts.append(f"{n}: {mask_text(ln.strip())[:160]}")
                items.append({"cat": "esteira", "name": rel, "file": os.path.relpath(p, os.path.dirname(top)).replace(os.sep, "/"), "line": 1,
                              "detail": f"{kind} · secrets do Actions: {', '.join(sorted(secrets)) or '-'}", "facts": facts, "repo": os.path.basename(top)})
    return items


# ── saida ───────────────────────────────────────────────────────────────────────────────────────────────────────────
def describe(r):
    keys = {"lambda": ["runtime", "handler", "memory", "timeout", "role", "modified", "envNames", "envRefs", "triggers", "logGroup"],
            "s3": ["bucketRegion", "versioning", "notifications", "website", "created"],
            "codebuild": ["sourceType", "sourceLocation", "buildspecFile", "image", "compute", "envNames", "envFromStore", "role", "logGroup", "webhook"],
            "secret": ["description", "changed", "accessed", "rotation", "tagKeys"],
            "log-group": ["retention", "storedMB"], "sqs": ["fifo", "visibility", "retentionDays", "deadLetter", "maxReceive", "messages", "inFlight"],
            "sns": ["subscriptions"], "events-rule": ["bus", "schedule", "pattern", "state", "targets"], "rds": ["engine", "class", "multiAz", "cluster", "dbName"],
            "cognito": ["id", "clients", "triggers"], "cloudfront": ["aliases", "origins", "comment"], "codepipeline": ["role", "artifactStore", "lastExecution"]}
    return {k: r[k] for k in keys.get(r["svc"], []) if r.get(k) not in (None, "", [], {})}


def write_outputs(out, account, region_names, resources, problems, linked, why, terms, repo_items, profile, module):
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, f"conta-{account}.json"), "w", encoding="utf-8") as fh:
        json.dump({"account": account, "profile": profile, "regions": region_names, "problems": problems, "resources": resources}, fh, ensure_ascii=False, indent=1)
    by = defaultdict(list)
    for r in resources:
        by[r["svc"]].append(r)
    L = [f"# Infra da conta {account} ({', '.join(region_names)})", "", f"Perfil do CLI: `{profile}` · somente leitura · {len(resources)} recursos", ""]
    L += ["| Servico | Qtde | Exemplos |", "|---|---|---|"]
    for svc in sorted(by, key=lambda s: -len(by[s])):
        L.append(f"| {LABEL.get(svc, svc)} | {len(by[svc])} | {', '.join(x['name'] for x in by[svc][:6])}{' …' if len(by[svc]) > 6 else ''} |")
    if problems["denied"]:
        L += ["", "## Sem permissao (nao lido — vira GAP no documento)", ""] + [f"- `{c}` — {m}" for c, m in problems["denied"]]
    if problems["failed"]:
        L += ["", "## Falhou", ""] + [f"- `{c}` — {m}" for c, m in problems["failed"]]
    open(os.path.join(out, "resumo.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")

    M = [f"# Infra de {module} (conta {account})", "", f"Termos usados para ligar: {', '.join(sorted(terms)) or '-'}", ""]
    groups = defaultdict(list)
    for i in linked:
        groups[resources[i]["svc"]].append(i)
    for svc in sorted(groups):
        M += [f"## {LABEL.get(svc, svc)}", ""]
        for i in groups[svc]:
            r = resources[i]
            M.append(f"### {r['name']}")
            M.append(f"- ligacao: {why.get(i, '')}")
            M.append(f"- arn: `{r['arn']}`")
            for k, v in describe(r).items():
                M.append(f"- {k}: {json.dumps(v, ensure_ascii=False)[:500]}")
            if r.get("buildspecInline"):
                M += ["- buildspec (mascarado):", "```yaml", r["buildspecInline"][:2500], "```"]
            if r.get("stages"):
                M.append("- estagios: " + " → ".join(f"{s['name']}[{', '.join(a['provider'] for a in s['actions'])}]" for s in r["stages"]))
                for s in r["stages"]:
                    for ac in s["actions"]:
                        if ac["config"]:
                            M.append(f"  - {s['name']}/{ac['name']}: {json.dumps(ac['config'], ensure_ascii=False)[:300]}")
            M.append("")
    # onde ver logs
    M += ["## Onde ver os logs (CloudWatch)", ""]
    lg = {r["name"]: r for r in resources if r["svc"] == "log-group"}
    seen = set()
    for i in linked:
        r = resources[i]
        g = r.get("logGroup")
        if g and g not in seen:
            seen.add(g)
            ex = f"existe, retencao {lg[g]['retention']}" if g in lg else "grupo ainda nao criado ou fora da conta/regiao lida"
            M.append(f"- `{r['svc']}:{r['name']}` → `{g}` ({ex}): `aws logs tail {g} --since 1h --follow --profile {profile} --region {region_names[0]}`")
    for g in sorted(n for n in lg if any(norm(t) in norm(n) for t in terms if len(t) >= 3) and n not in seen):
        M.append(f"- `{g}` (retencao {lg[g]['retention']}): `aws logs tail {g} --since 1h --profile {profile} --region {region_names[0]}`")
    M += ["", "Logs Insights (console CloudWatch → Logs Insights → escolha o grupo): `fields @timestamp, @message | filter @message like /ERROR/ | sort @timestamp desc | limit 50`", ""]
    if repo_items:
        M += ["## Esteiras nos repositorios", ""]
        for it in repo_items:
            M += [f"### {it['name']} ({it['repo']})", f"- {it['detail']}"] + [f"  - {f}" for f in it["facts"][:25]] + [""]
    open(os.path.join(out, "modulo.md"), "w", encoding="utf-8").write("\n".join(M) + "\n")


def build_inventory(out_json, account, region_names, resources, linked, why, repo_items, profile):
    inv = []
    for i in linked:
        r = resources[i]
        det = json.dumps(describe(r), ensure_ascii=False)[:180]
        inv.append({"cat": "aws-" + r["svc"], "name": r["name"], "file": f"infra/conta-{account}.json", "line": 1,
                    "detail": f"aws {account}/{r.get('bucketRegion') or region_names[0]} · {why.get(i, '')} · {det}"})
    inv += [{k: it[k] for k in ("cat", "name", "file", "line", "detail")} for it in repo_items]
    counts = defaultdict(int)
    for it in inv:
        counts[it["cat"]] += 1
    json.dump({"version": 1, "sources": [{"role": "aws", "path": f"{account}:{profile}"}], "files": {}, "counts": dict(counts), "items": inv},
              open(out_json, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return counts


def resolve_profile(account, wanted):
    """Perfil do CLI cuja conta e a pedida (a conta e o que a configuracao do PRMake guarda, nao o nome do perfil)."""
    profiles = subprocess.run(["aws", "configure", "list-profiles"], capture_output=True, text=True).stdout.split()
    order = ([wanted] if wanted else []) + [p for p in profiles if p != wanted]
    for p in order:
        r = subprocess.run(["aws", "sts", "get-caller-identity", "--profile", p, "--output", "json"], capture_output=True, text=True, timeout=30)
        if r.returncode == 0:
            ident = json.loads(r.stdout)
            if not account or ident["Account"] == account:
                return p, ident
    return None, None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--inventario")
    ap.add_argument("--settings", help="settings.json do PRMake (infra.accounts / infra.regions)")
    ap.add_argument("--account")
    ap.add_argument("--profile")
    ap.add_argument("--region", action="append", default=[])
    ap.add_argument("--sigla", default="")
    ap.add_argument("--termo", action="append", default=[], help="termo extra que liga recurso ao modulo")
    ap.add_argument("--recurso", action="append", default=[], help="ARN/nome de recurso a incluir de qualquer forma")
    ap.add_argument("--so-conta", action="store_true", help="so mapear a conta (sem ligar ao modulo)")
    args = ap.parse_args()

    cfg = {}
    if args.settings and os.path.exists(args.settings):
        cfg = (json.load(open(args.settings, encoding="utf-8")).get("infra") or {})
    accounts = [args.account] if args.account else [x.get("id") if isinstance(x, dict) else x for x in cfg.get("accounts", [])] or [None]
    regions = args.region or cfg.get("regions") or ["us-east-1"]
    inventario = json.load(open(args.inventario, encoding="utf-8")) if args.inventario and os.path.exists(args.inventario) else {}

    all_items, any_ok = [], False
    for account in accounts:
        profile, ident = resolve_profile(account, args.profile)
        if not profile:
            print(f"ERRO: nenhum perfil do AWS CLI entra na conta {account or '(qualquer)'} — rode `aws configure list-profiles` e `aws sts get-caller-identity --profile <p>`", file=sys.stderr)
            continue
        acct = ident["Account"]
        print(f"Conta {acct} · perfil {profile} · {ident['Arn'].split('/')[-1]} · regioes {', '.join(regions)}")
        resources, problems = [], {"denied": [], "failed": []}
        for region in regions:
            a = Aws(profile, region)
            a.account, a.primary_region = acct, regions[0]
            def run(entry, a=a):
                name, fn = entry
                try:
                    return fn(a) or []
                except Exception as e:  # um servico nao derruba o resto
                    a.failed.append((name, f"{type(e).__name__}: {e}"[:200]))
                    return []
            for got in pmap(run, COLLECTORS, workers=4):  # servicos em paralelo (cada chamada do CLI custa ~1 s)
                for r in got:
                    r["region"] = region
                resources += got
            problems["denied"] += [(f"{region} {c}", m) for c, m in a.denied]
            problems["failed"] += [(f"{region} {c}", m) for c, m in a.failed]
        any_ok = True
        ts, code = terms_for(args.module, args.sigla, inventario, args.termo)
        linked, why = ([], {}) if args.so_conta else link(resources, ts, code, set(args.recurso) | {r["name"] for r in resources if r["name"] in args.recurso})
        all_items.append((acct, profile, resources, problems, linked, why, ts))
        print(f"  {len(resources)} recursos na conta · {len(linked)} ligados a {args.module}" + (f" · {len(problems['denied'])} leituras sem permissao" if problems["denied"] else ""))

    if not any_ok:
        return 3
    sources = [(x["role"], x["path"]) for x in inventario.get("sources", []) if os.path.exists(x.get("path", ""))]
    repo_items = scan_repo_pipelines(sources)
    os.makedirs(args.out, exist_ok=True)
    total = defaultdict(int)
    merged = {"version": 1, "sources": [], "files": {}, "counts": {}, "items": []}
    for acct, profile, resources, problems, linked, why, ts in all_items:
        write_outputs(args.out, acct, regions, resources, problems, linked, why, ts, repo_items if acct == all_items[0][0] else [], profile, args.module)
        tmp = os.path.join(args.out, f".inv-{acct}.json")
        build_inventory(tmp, acct, regions, resources, linked, why, repo_items if acct == all_items[0][0] else [], profile)
        part = json.load(open(tmp, encoding="utf-8"))
        os.remove(tmp)
        merged["sources"] += part["sources"]
        merged["items"] += part["items"]
    for it in merged["items"]:
        total[it["cat"]] += 1
    merged["counts"] = dict(total)
    json.dump(merged, open(os.path.join(os.path.dirname(args.out.rstrip("/")), "inventario-infra.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("Infra: " + ", ".join(f"{n} {k}" for k, n in sorted(total.items(), key=lambda x: -x[1])) + f" -> {args.out}")
    if any(p["denied"] for _, _, _, p, _, _, _ in all_items):
        print("AVISO: parte dos servicos nao pode ser lida (sem permissao) — veja resumo.md; vira GAP no documento")
    return 0


if __name__ == "__main__":
    sys.exit(main())
