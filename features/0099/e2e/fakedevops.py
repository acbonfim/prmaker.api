# DevOps falso (0099 Q1): formato da REST 7.1, estado em memória, log de escritas em /log.
import http.server, json, re, sys, threading, urllib.parse
from datetime import datetime, timezone
LOCK = threading.Lock(); LOG = []
COL, LANE, DONE = "WEF_AAA_Kanban.Column", "WEF_AAA_Kanban.Lane", "WEF_AAA_Kanban.Column.Done"
P = "Solvace Product Improvement"; AREA = P + "\\Mesa"
TYPES = [{"name": "Bug", "color": "CC293D", "icon": {"id": "icon_insect", "url": ""}, "description": "Bug", "isDisabled": False},
         {"name": "User Story", "color": "009CCC", "icon": {"id": "icon_book", "url": ""}, "description": "US", "isDisabled": False},
         {"name": "Task", "color": "F2CB1D", "icon": {"id": "icon_clipboard", "url": ""}, "description": "Task", "isDisabled": False},
         {"name": "Epic", "color": "FF7B00", "icon": {"id": "icon_crown", "url": ""}, "description": "Epic", "isDisabled": False}]
COLUMNS = [
  {"id": "c1", "name": "New", "itemLimit": 0, "columnType": "incoming", "isSplit": False, "description": "", "stateMappings": {"Bug": "New", "User Story": "New"}},
  {"id": "c2", "name": "Active", "itemLimit": 2, "columnType": "inProgress", "isSplit": True, "description": "Em desenvolvimento", "stateMappings": {"Bug": "Active", "User Story": "Active"}},
  {"id": "c3", "name": "Dev Test in QA", "itemLimit": 5, "columnType": "inProgress", "isSplit": False, "description": "", "stateMappings": {"Bug": "Resolved", "User Story": "Resolved"}},
  {"id": "c4", "name": "Closed", "itemLimit": 0, "columnType": "outgoing", "isSplit": False, "description": "", "stateMappings": {"Bug": "Closed", "User Story": "Closed"}}]
ROWS = [{"id": "00000000-0000-0000-0000-000000000000", "name": None}, {"id": "r2", "name": "Expedite"}]
TAGS = ["cliente-gerdau", "performance", "rca", "regressao", "ux"]
PEOPLE = [{"displayName": "Bruno Ferreira", "uniqueName": "bruno@solvace.com", "id": "u1", "imageUrl": ""},
          {"displayName": "Daniel Strinta", "uniqueName": "daniel@solvace.com", "id": "u2", "imageUrl": ""}]
def now(): return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")
ITEMS = {}; NEXT = [90001]
def mk(type_, title, state, col, lane=None, done=False, tags=(), who=None, rank=1.0, parent=None):
    i = NEXT[0]; NEXT[0] += 1
    f = {"System.Id": i, "System.WorkItemType": type_, "System.Title": title, "System.State": state, "System.AreaPath": AREA,
         "System.IterationPath": P + "\\Sprint 1", "System.TeamProject": P, "System.CreatedDate": now(), "System.ChangedDate": now(),
         "System.Tags": "; ".join(tags), COL: col, LANE: lane or "", DONE: done, "System.BoardColumn": col, "System.BoardColumnDone": done,
         "System.BoardLane": lane or "", "Microsoft.VSTS.Common.StackRank": rank, "Microsoft.VSTS.Common.Priority": 2,
         "Microsoft.VSTS.Common.Severity": "3 - Medium" if type_ == "Bug" else None}
    if who: f["System.AssignedTo"] = {"displayName": who["displayName"], "uniqueName": who["uniqueName"], "id": who["id"], "imageUrl": ""}
    if parent: f["System.Parent"] = parent
    ITEMS[i] = {"id": i, "rev": 1, "fields": {k: v for k, v in f.items() if v is not None}, "relations": []}
    return i
mk("Bug", "Erro ao salvar checklist offline", "New", "New", tags=["cliente-gerdau"], who=PEOPLE[0], rank=10)
mk("Bug", "Lentidão no dashboard de KPIs", "Active", "Active", tags=["performance"], who=PEOPLE[1], rank=20)
mk("User Story", "Exportar relatório de anomalias em PDF", "Active", "Active", done=True, rank=30)
mk("Bug", "Botão salvar duplicado na tela de auditoria", "New", "New", lane="Expedite", tags=["ux", "regressao"], rank=40)
mk("User Story", "Filtro por planta no relatório", "Resolved", "Dev Test in QA", who=PEOPLE[0], rank=50)
mk("Bug", "Erro ao salvar checklist sem internet", "Closed", "Closed", tags=["cliente-gerdau"], rank=60)
FIELDS = [("System.Title", "Title", "string", False), ("System.State", "State", "string", False), ("System.AssignedTo", "Assigned To", "string", False),
          ("System.AreaPath", "Area Path", "treePath", False), ("System.IterationPath", "Iteration Path", "treePath", False),
          ("System.Tags", "Tags", "plainText", False), ("System.Description", "Description", "html", False),
          ("Microsoft.VSTS.Common.Priority", "Priority", "integer", False), ("Microsoft.VSTS.Common.Severity", "Severity", "string", False),
          ("Microsoft.VSTS.TCM.ReproSteps", "Repro Steps", "html", False), ("System.Id", "ID", "integer", True),
          ("System.CreatedDate", "Created Date", "dateTime", True), ("Custom.BugType", "Bug Type", "string", False)]
ALLOWED = {"System.State": {"Bug": ["New", "Active", "Resolved", "Closed"], "User Story": ["New", "Active", "Resolved", "Closed"], "Task": ["To Do", "Doing", "Done"], "Epic": ["New", "Active", "Closed"]},
           "Microsoft.VSTS.Common.Priority": [1, 2, 3, 4], "Microsoft.VSTS.Common.Severity": ["1 - Critical", "2 - High", "3 - Medium", "4 - Low"],
           "Custom.BugType": ["Code", "Data", "Configuration", "Infra"]}
def type_fields(t):
    out = []
    for ref, name, typ, ro in FIELDS:
        if ref in ("Microsoft.VSTS.TCM.ReproSteps", "Microsoft.VSTS.Common.Severity", "Custom.BugType") and t != "Bug": continue
        av = ALLOWED.get(ref); av = av.get(t, []) if isinstance(av, dict) else (av or [])
        out.append({"referenceName": ref, "name": name, "alwaysRequired": ref == "System.Title", "allowedValues": av,
                    "defaultValue": "New" if ref == "System.State" else None, "helpText": ""})
    return out
def view(it, fields=None):
    f = it["fields"] if not fields else {k: v for k, v in it["fields"].items() if k in fields}
    return {"id": it["id"], "rev": it["rev"], "fields": f, "relations": it["relations"],
            "url": f"https://dev.azure.com/solvacelabs/_apis/wit/workItems/{it['id']}"}
def apply_patch(it, ops):
    for op in ops:
        path = op.get("path", ""); val = op.get("value")
        if path.startswith("/fields/"):
            ref = path[len("/fields/"):]
            if op.get("op") == "remove": it["fields"].pop(ref, None); continue
            if ref == "System.State":
                allowed = ALLOWED["System.State"].get(it["fields"]["System.WorkItemType"], [])
                if val not in allowed: raise ValueError(f"TF401320: Rule Error for field State. Error code: Required, InvalidListValue. '{val}'")
            if ref == "System.AssignedTo" and isinstance(val, str):
                p = next((x for x in PEOPLE if val in (x["uniqueName"], x["displayName"])), None)
                val = {"displayName": p["displayName"], "uniqueName": p["uniqueName"], "id": p["id"]} if p else {"displayName": val, "uniqueName": val}
            it["fields"][ref] = val
            if ref == COL: it["fields"]["System.BoardColumn"] = val
            if ref == DONE: it["fields"]["System.BoardColumnDone"] = bool(val)
            if ref == LANE: it["fields"]["System.BoardLane"] = val or ""
        elif path == "/relations/-":
            it["relations"].append(val)
            m = re.search(r"/(\d+)$", val.get("url", ""))
            if m and val.get("rel") == "System.LinkTypes.Hierarchy-Reverse": it["fields"]["System.Parent"] = int(m.group(1))
        elif path == "/rev": pass
    it["rev"] += 1; it["fields"]["System.ChangedDate"] = now()
class H(http.server.BaseHTTPRequestHandler):
    def send(self, code, obj):
        b = json.dumps(obj).encode(); self.send_response(code); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(b))); self.end_headers(); self.wfile.write(b)
    def body(self):
        n = int(self.headers.get("Content-Length") or 0); return json.loads(self.rfile.read(n) or b"null") if n else None
    def route(self, method):
        u = urllib.parse.urlparse(self.path); path = urllib.parse.unquote(u.path); q = urllib.parse.parse_qs(u.query)
        if path == "/log": return self.send(200, LOG)
        if path == "/items": return self.send(200, list(ITEMS.values()))
        with LOCK:
            if method != "GET": LOG.append({"m": method, "p": path, "q": u.query})
            m = re.match(r"^/solvacelabs/_apis/projects/([^/]+)/teams/([^/]+)/members$", path)
            if m: return self.send(200, {"value": [{"identity": p} for p in PEOPLE]})
            if re.match(r"^/solvacelabs/_apis/projects/([^/]+)/teams$", path):
                return self.send(200, {"value": [{"id": "t1", "name": "Mesa Team"}, {"id": "t2", "name": "Core Team"}]})
            if re.match(r"^/solvacelabs/_apis/projects/([^/]+)$", path):
                return self.send(200, {"id": "p1", "name": P, "defaultTeam": {"id": "t1", "name": "Mesa Team"}})
            m = re.match(r"^/solvacelabs/([^/]+)/([^/]+)/_apis/work/(.*)$", path)
            if m:
                team, rest = m.group(2), m.group(3)
                if rest == "boards": return self.send(200, {"value": [{"id": "b1", "name": "Stories"}, {"id": "b2", "name": "Epics"}]})
                if rest.startswith("boards/"):
                    return self.send(200, {"id": "b1", "name": "Stories", "columns": COLUMNS, "rows": ROWS,
                        "fields": {"columnField": {"referenceName": COL}, "rowField": {"referenceName": LANE}, "doneField": {"referenceName": DONE}}})
                if rest == "teamsettings/teamfieldvalues":
                    return self.send(200, {"defaultValue": AREA, "field": {"referenceName": "System.AreaPath"}, "values": [{"value": AREA, "includeChildren": True}]})
                if rest == "backlogconfiguration":
                    return self.send(200, {"backlogFields": {"typeFields": {"Order": "Microsoft.VSTS.Common.StackRank"}},
                        "requirementBacklog": {"name": "Stories", "workItemTypes": [{"name": "User Story"}, {"name": "Bug"}]}})
                if rest == "workitemsorder":
                    b = self.body() or {}; ids = b.get("ids", []); prev, nxt = b.get("previousId"), b.get("nextId")
                    ranks = {i: ITEMS[i]["fields"].get("Microsoft.VSTS.Common.StackRank", 0) for i in ITEMS}
                    lo = ranks.get(prev, None); hi = ranks.get(nxt, None)
                    r = (lo + hi) / 2 if lo is not None and hi is not None else (lo + 1 if lo is not None else (hi - 1 if hi is not None else 1))
                    for i in ids: ITEMS[int(i)]["fields"]["Microsoft.VSTS.Common.StackRank"] = r
                    LOG[-1]["body"] = b
                    return self.send(200, {"value": [{"id": int(i), "order": r} for i in ids]})
            m = re.match(r"^/solvacelabs/([^/]+)/_apis/wit/(.*)$", path)
            if m:
                rest = m.group(2)
                if rest == "workitemtypes": return self.send(200, {"value": TYPES})
                mm = re.match(r"^workitemtypes/([^/]+)/fields$", rest)
                if mm: return self.send(200, {"value": type_fields(mm.group(1))})
                if rest == "fields": return self.send(200, {"value": [{"referenceName": r, "name": n, "type": t, "readOnly": ro, "isIdentity": r == "System.AssignedTo"} for r, n, t, ro in FIELDS]})
                if rest == "tags": return self.send(200, {"value": [{"id": f"tg{i}", "name": t} for i, t in enumerate(TAGS)]})
                if rest == "classificationnodes":
                    return self.send(200, {"value": [
                        {"name": P, "structureType": "area", "path": "\\" + P + "\\Area", "children": [{"name": "Mesa", "structureType": "area", "path": "\\" + P + "\\Area\\Mesa"}, {"name": "Core", "structureType": "area", "path": "\\" + P + "\\Area\\Core"}]},
                        {"name": P, "structureType": "iteration", "path": "\\" + P + "\\Iteration", "children": [{"name": "Sprint 1", "structureType": "iteration", "path": "\\" + P + "\\Iteration\\Sprint 1"}, {"name": "Sprint 2", "structureType": "iteration", "path": "\\" + P + "\\Iteration\\Sprint 2"}]}]})
                if rest == "wiql":
                    b = self.body() or {}; LOG[-1]["wiql"] = b.get("query")
                    qy = b.get("query") or ""; ids = sorted(ITEMS)
                    mi = re.search(r"\[System\.Id\] IN \(([\d, ]+)\)", qy)
                    if mi: ids = [i for i in ids if str(i) in [x.strip() for x in mi.group(1).split(",")]]
                    ma = re.search(r"\[System\.AreaPath\] UNDER '([^']+)'", qy)
                    if ma: ids = [i for i in ids if ITEMS[i]["fields"].get("System.AreaPath", "").startswith(ma.group(1))]
                    sts = set(re.findall(r"'(New|Active|Resolved|Closed)'", qy))
                    if sts: ids = [i for i in ids if ITEMS[i]["fields"]["System.State"] in sts]
                    return self.send(200, {"queryType": "flat", "workItems": [{"id": i, "url": ""} for i in ids]})
                if rest == "workitemsbatch":
                    b = self.body() or {}
                    return self.send(200, {"count": 0, "value": [view(ITEMS[i], b.get("fields")) for i in b.get("ids", []) if i in ITEMS]})
                mm = re.match(r"^workItems/(\d+)/comments$", rest, re.I)
                if mm: LOG[-1]["body"] = self.body(); return self.send(200, {"id": 7, "text": "ok"})
                mm = re.match(r"^workitems/\$(.+)$", rest)
                if mm and method == "POST":
                    ops = self.body() or []; t = mm.group(1)
                    i = mk(t, "", "New", "New"); it = ITEMS[i]; it["fields"]["System.AreaPath"] = P
                    try: apply_patch(it, ops)
                    except ValueError as e: del ITEMS[i]; return self.send(400, {"message": str(e)})
                    it["rev"] = 1; LOG[-1]["body"] = ops
                    return self.send(200, view(it))
                mm = re.match(r"^workitems/(\d+)$", rest)
                if mm:
                    i = int(mm.group(1))
                    if i not in ITEMS: return self.send(404, {"message": f"TF401232: Work item {i} does not exist."})
                    if method == "PATCH":
                        ops = self.body() or []; LOG[-1]["body"] = ops
                        try: apply_patch(ITEMS[i], ops)
                        except ValueError as e: return self.send(400, {"message": str(e)})
                    return self.send(200, view(ITEMS[i]))
            return self.send(404, {"message": f"fake: rota desconhecida {method} {path}"})
    def do_GET(self): self.route("GET")
    def do_POST(self): self.route("POST")
    def do_PATCH(self): self.route("PATCH")
    def log_message(self, *a): pass
http.server.ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1])), H).serve_forever()
