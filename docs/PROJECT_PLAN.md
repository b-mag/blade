# BLADE — Sheriff's Office AI Integration POC

> **BLADE: Bureau Linked Analytics & Data Explorer**, built for the fictional *Kestrel County Sheriff's Office*.
> Status: **PLAN v0.2, decisions locked** (2026-11-14). Demo target: ~2026-11-16. Audience: agency IT/technical staff.

---

## 1. What we must prove in the room

| # | Goal | How the POC proves it |
|---|------|-----------------------|
| 1 | **We can deliver real value** | Two or three *live* AI features against realistic data: natural-language investigative search, policy Q&A with citations, link analysis (GraphRAG). |
| 2 | **We know how to keep it secure and auditable** | A Home tab that walks through the security architecture, plus a few *real but lightweight* controls in the POC: an audit log tab, a role switcher, citation verification, and a "how I read your question" panel. |
| 3 | **We have more ROI ideas** | A set of "Opportunity" tabs, each written to the same template: problem, AI approach, local tech, data needed, ROI, risks, effort. |

**Key message:** *"Everything you see can run on a box in your server room. For this demo it is running on this laptop, and here is the proof."* (Unplug Wi-Fi mid-demo; it keeps working.)

---

## 2. Hardware reality check (this laptop)

| Component | Value | Verdict |
|---|---|---|
| GPU | NVIDIA RTX 4060 Laptop, **8 GB VRAM** | Runs 7–8B models at 4-bit quantization fully on the GPU (~30–50 tokens/sec) |
| RAM | 16 GB | OK. Keep VS + Docker + browser lean during the demo |
| CPU | Ryzen 7 7435HS (8C/16T) | Fine |
| Disk | ~24 GB free | Enough for 1–2 models (~5 GB each) plus an embedding model (~0.3 GB) |
| .NET | SDK 10.0.302 | Good. Target `net10.0-windows` |

**Recommendation:** make **local Ollama the primary provider**. Keep a free cloud endpoint (OpenAI-compatible) as a *config-switch fallback only*. Running fully local is our strongest selling point, and this machine can do it.

Candidate models (check which is current when we install):
- Chat / tool-calling: `qwen2.5:7b-instruct` or `llama3.1:8b` (Qwen3 8B also works if available)
- Embeddings: `nomic-embed-text` (small, fast, good retrieval quality)

### 2.1 Benchmark results (2026-11-14, Ollama 0.40.16, `qwen3:8b` + `nomic-embed-text`)

Reproduce with `tools/benchmark-ollama.ps1`.

| Measure | Result | Meaning for the demo |
|---|---|---|
| GPU fit | qwen3:8b **5.2 GB, 100% on GPU**; with the embedding model loaded, 7.0 of 8.2 GB VRAM in use | Both models stay resident. Close other GPU apps during the demo |
| Generation speed (warm) | **~35–45 tokens/sec** | A 200-word answer in about 5 s. Feels live |
| Prompt processing | ~1,300–1,400 tokens/sec | A RAG context of 3,000 tokens adds about 2 s |
| NL → search filter | **~2–4 s per question** | Fast enough to show the "how I read your question" chips |
| Policy answer with citations | ~1.6 s; cited correctly, and said plainly when the policy did not cover part of the question | The grounding prompt works |
| Embeddings (warm) | ~76 chunks/sec, 768 dimensions | Embedding all ~2,000 synthetic narratives plus policy chunks takes under a minute |
| **First-ever cold load** | **75 s** (plus 47 s for the embedding model) | **Must warm up before the demo** |
| Reload from disk cache | ~19 s | Set `keep_alive = -1` so models never unload |

**Design lessons learned (folded into §4.1):**
1. **Don't let an 8B model do date math.** When the model returned absolute dates, it got "last 18 months" wrong (it gave 6 months) and read "after 10pm" as 10:00. Fix: the model returns *relative* values (`lookback_months: 18`, `hour_from: 22`, `hour_to: 6`), and **C# computes the actual dates**. After the change, 7 of 7 test questions were correct.
2. **Cap list lengths in the JSON schema** (`maxItems`). Without a cap, the model sometimes repeated a tag until it hit the token limit, producing invalid JSON. Also set `num_predict` and de-duplicate in code.
3. **Few-shot example in the system prompt** noticeably improves enum mapping.
4. Use `think: false` with qwen3 for these tasks (lower latency; reasoning isn't needed for extraction).
5. Synonym gaps ("car break-in" → `THEFT` instead of `THEFT_FROM_VEHICLE`) are fixed by adding synonyms to the prompt. The editable filter chips also let the user correct them, which is good to show as a "human in the loop" moment.

**Conclusion: run fully local for the demo.** The cloud fallback stays a config switch we hope never to use.

---

## 3. Architecture

```
┌──────────────────────────────┐        HTTPS (localhost in POC)       ┌──────────────────────────────────────────┐
│  Blade.Desktop (WPF, .NET 10)│  ───────────────────────────────────▶ │  Blade.Server (ASP.NET Core Minimal API)  │
│  - Tabs / UI only            │                                       │  - Auth stub + role context               │
│  - Never talks to an LLM     │ ◀───────────────────────────────────  │  - Audit logging middleware               │
│  - WebView2 for graph viz    │      answers + citations + trace      │  - Query planner (NL → structured filter) │
└──────────────────────────────┘                                       │  - Retrieval (SQL + vector + graph)       │
                                                                       │  - Citation guard / redaction hooks       │
                                                                       └───────┬──────────────┬───────────────────┘
                                                                               │              │
                                                     ┌─────────────────────────▼───┐   ┌──────▼────────────────────────┐
                                                     │ SQLite: RMS/CAD/Jail tables │   │ AI provider (IChatClient)      │
                                                     │ + embeddings + graph edges  │   │  • Ollama (local)  ◀─ default  │
                                                     │ Policy docs (markdown)      │   │  • OpenAI-compatible (fallback)│
                                                     └─────────────────────────────┘   └───────────────────────────────┘
```

**Why a separate server even in the POC?** Because that is how it would be deployed. The client is a thin UI, and the server is where RBAC, audit, redaction and model access are enforced. No workstation ever holds model keys or talks to a model directly. This is a key talking point for the technical audience.

### Tech choices

| Concern | POC | Production equivalent (what we'd say) |
|---|---|---|
| Desktop UI | WPF + .NET 10 (Fluent theme) | Same, or web client behind agency SSO |
| AI abstraction | `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`) | Same. The provider is swappable by config |
| Local LLM runtime | Ollama (via OllamaSharp) | Ollama / vLLM / NVIDIA NIM on an agency GPU server |
| Structured data | SQLite | Existing RMS/CAD/JMS (SQL Server, Oracle, etc.) through read-only replicas |
| Vector search | Embeddings in SQLite + in-memory cosine (a few thousand chunks is trivial) | SQL Server 2025 native `VECTOR` type, pgvector, or Qdrant |
| Graph | Entity/edge tables in SQLite, traversed in memory | Neo4j / SQL Server graph tables |
| Graph visualization | WebView2 + Cytoscape.js (**bundled locally, no CDN**) | Same |
| Auth | Role switcher (Deputy / Analyst / Supervisor / Admin) | AD / Entra ID on-prem, smart card / MFA (CJIS AAL) |

---

## 4. The AI integrations (live in the POC)

### 4.1 Ask BLADE: natural-language investigative search *(MUST)*
> *"Show similar burglaries in the north district last 18 months involving forced entry on commercial buildings after 10pm"*

Pipeline:
1. **Interpret.** The LLM turns the question into a **typed JSON filter** (offense type, district, *relative* period, hour window, premise type, MO tags) using a JSON-schema constrained output. The server converts relative periods to dates (see §2.1 lessons).
2. **Show its reading.** The UI displays the filter as chips ("District: North · Since 2025-05-14 · After 22:00 · Commercial · Forced entry"). The user can see and edit the AI's interpretation. *Transparency is a selling point.*
3. **Retrieve.** The server runs **parameterized SQL** (the LLM never writes raw SQL, which prevents injection and keeps RBAC enforceable). Results are re-ranked by vector similarity on the narratives.
4. **Answer.** The LLM summarizes the patterns and cites case numbers, e.g. `[25-004512]`.
5. **Citation guard.** The server checks every cited ID against the retrieved set and flags or strips anything it doesn't recognize. *("The AI cannot cite a report it wasn't given.")*
6. **Results grid.** Report #, date/time, address, district, link to open the full report.

### 4.2 Policy Assistant: RAG over the policy manual *(MUST)*
> *"What does policy say about use of force with a knife threat when the subject is non-compliant?"*

- A fictional policy manual (~25–40 sections) in a Lexipol-*style* structure, written from scratch: use of force, pursuits, body cameras, evidence, juveniles, DV, mental health crisis, etc.
- Chunked by section and subsection, embedded, retrieved top-k, answered with **section citations** (`Policy 300.3.2`). Clicking a citation opens the exact paragraph highlighted.
- Guardrail prompt: answer only from the retrieved policy. If the policy doesn't cover it, say so. The answer always carries a disclaimer: *"Policy reference, not legal advice; confirm with your supervisor."*

### 4.3 Link Explorer: GraphRAG *(MUST, the "wow" moment)*
- Entities: **Person, Vehicle, Location, Incident, Phone, Organization/Business, Pawn Item, Jail Booking**.
- Edges: suspect-in, victim-in, witness-in, registered-owner, observed-at (FI card / LPR hit), booked-on, lives-at, associated-with.
- The question returns a subgraph: *"What connects the North district commercial burglaries?"* → a white van (plate X) seen at 3 scenes → registered to a person → that person's booking record → their associate's pawn transactions.
- The LLM narrates the path *with citations on every hop*.
- **Planted storylines** in the synthetic data make this reliably demoable (see §5).

### 4.4 Report Narrative Assistant *(SHOULD, cheap and high ROI)*
- The deputy enters structured fields and bullet notes. The LLM drafts a narrative in agency style.
- A **Quality/Consistency checker** button flags issues: time on the face sheet ≠ time in the narrative, missing elements of the offense, vague language ("the suspect was acting suspicious"), missing Miranda notation, etc.
- Talking point: the deputy remains the author and signs the report. AI output is clearly marked as a draft, and the original notes are retained.

### 4.5 Audit Log *(MUST, small but proves Goal #2)*
- Every AI request is logged: user, role, timestamp, tab, prompt, interpreted filter, records retrieved, records cited, model + version, latency, token counts, provider (local/cloud).
- Grid with filters + "usage per user" summary chart.
- Talking point: "This is what your CJIS auditor sees."

---

## 5. Synthetic data plan (fictional agency)

All data is generated by `Blade.DataGen` (deterministic seed → reproducible). Nothing is real. Every screen shows a **"SYNTHETIC DATA — FICTIONAL AGENCY"** watermark.

| Dataset | Volume | Type | Notes |
|---|---|---|---|
| Incidents (RMS) | ~1,500 over 24 months | Structured + narrative text | NIBRS-ish offense codes, 4 districts, premise types, MO tags, time of day |
| CAD calls for service | ~4,000 | Structured | Some link to incidents |
| Persons | ~900 | Structured | Fake names, DOBs. *Use these to show PII redaction in concept discussions* |
| Vehicles | ~450 | Structured | Plates, make/model/color |
| Locations / addresses | ~600 | Structured | Fictional streets, geocoded to a fictional grid |
| Field Interview cards | ~300 | Semi-structured | Great for link analysis |
| Jail bookings | ~500 | Structured + notes | Charges, housing, classification notes |
| Pawn transactions | ~200 | Structured | Links stolen property to people |
| Policy manual | ~30 sections | Unstructured (markdown) | Written from scratch for the fictional agency |
| "Legacy" case files | ~10 | Scanned-look images/PDFs | Visual prop for the OCR concept tab (OCR live is a stretch goal) |

**Planted storylines** (so the demo works every time):
1. **North District commercial burglary series:** 7 burglaries after 22:00, forced rear entry, pry tool; a white Ford Transit seen in 3 FI/LPR records; it ties to a person with a prior booking and pawned power tools.
2. **Catalytic converter thefts:** same suspect phone number appears in two unrelated reports plus a pawn record.
3. **Repeat DV address:** 5 calls, escalating language in narratives. Good for the "Pattern Explorer" and officer-safety flag concept.
4. **Use-of-force incident** with a knife-armed, non-compliant subject. Lets the Policy Assistant answer and also cite a matching historical case.

Narratives: templated with heavy variation. The storyline cases (≈40) get richer, LLM-polished narratives, generated once at build time and cached.

---

## 6. Desktop tabs

| Tab | Type | Content |
|---|---|---|
| **Home: Security & Governance** | Static (rich) | Security architecture bullets (draft list below), the POC-vs-production honesty statement, the architecture diagram |
| **Architecture & Deployment** | Static (rich) | Production topology, hardware sizing, integration patterns, model lifecycle (see §10) |
| **Ask BLADE** | LIVE | §4.1 |
| **Policy Assistant** | LIVE | §4.2 |
| **Link Explorer** | LIVE | §4.3 |
| **Report Assistant** | LIVE (should) | §4.4 drafting + quality check |
| **Audit Log** | LIVE | §4.5 |
| **Legacy Data Resurrection** | Concept | Local OCR (Tesseract / PaddleOCR / Azure Doc Intelligence *container*) → entity extraction → searchable. Before/after visual of a "typed 1994 report" |
| **Jail / Inmate Intelligence** | Concept | Booking summaries, classification consistency, release-date checks, keep-separate alerts, medical-flag surfacing (HIPAA caution) |
| **Pattern Explorer / Internal Analytics** | Concept + light visuals | Hot-spot/time heatmaps from synthetic data; "series detection" via clustering on MO embeddings |
| **Training & Historical Cases KB** | Concept | Searchable training bulletins, case law updates, lessons learned |
| **Public Records Redaction** | Concept *(high ROI)* | AI-assisted redaction of reports and body-cam transcripts for records requests. A human approves every redaction |
| **Body-cam / Interview Transcription** | Concept *(high ROI)* | Local Whisper transcription → searchable, timestamped, summarized; feeds the Report Assistant |
| **Dispatch / CAD Assist** | Concept | Live call summarization, premise history surfacing, officer-safety flags |
| **Court / Case Packet Builder** | Concept | Assemble the case file, timeline and evidence list for the DA. A big time-saver for detectives |
| **Opportunities & ROI** | Static summary | The 5 recommended next projects ranked by ROI × effort |

Each Concept tab follows one template:
**Problem today → What AI does → How it runs locally → Data it needs → Estimated ROI → Risks & guardrails → Effort (S/M/L)**

### Draft Home-tab security list (to refine)
- **Data sovereignty:** all models, data, embeddings and logs on agency-controlled hardware. No cloud inference, no telemetry, offline-capable. *(POC exception disclosed on screen if the cloud fallback is ever used.)*
- **CJIS Security Policy alignment** (v5.9.x / v6): advanced authentication, encryption in transit (TLS 1.2+) and at rest (FIPS 140-validated), audit, personnel screening for anyone with system access, CJIS Security Addendum for vendor staff.
- **Identity & RBAC:** AD/Entra-backed SSO with MFA. Roles and *data-level* permissions (sealed/expunged records, juvenile records, internal affairs, confidential informants). Retrieval is filtered *before* the LLM sees anything.
- **Full audit trail:** every prompt, retrieved record, generated answer, model version, user, workstation, timestamp. Tamper-evident (append-only/hash-chained), retained per records schedule, exportable for audits.
- **Usage monitoring:** per-user and per-unit dashboards, anomaly alerts (bulk queries, off-hours spikes, searches on specific persons with no case number → "need-to-know" prompts).
- **PII / CJI redaction layer:** configurable redaction of SSNs, DLs, juvenile identities, victim info (Marsy's Law states) on output and in logs where appropriate.
- **Grounded answers only:** retrieval-augmented, citation-required, citation-verified. "I don't know" is an acceptable answer. No answer without sources.
- **Human in the loop:** AI drafts and suggests; humans decide, sign and approve. Nothing AI-generated is filed automatically.
- **Prompt-injection defense:** treat document content as data; system-prompt isolation; output filtering; no tool access to write operations.
- **Model governance:** approved-model registry, versions pinned, evaluation test sets (accuracy, bias, hallucination rate) re-run on every model change. Change-control board.
- **Disclosure & legal:** AI-assisted content is labeled; Brady/Giglio and discovery implications reviewed with the DA's office; policy on AI use published.
- **Bias & civil-liberties review:** no predictive "who will commit crime" scoring; pattern tools focus on incidents and places, with periodic review.
- **Network isolation:** model server on a segmented VLAN; no outbound internet; patching via controlled process.
- **Least-privilege data access:** read-only replicas of RMS/CAD/JMS; the AI never writes to systems of record.

---

## 7. Solution layout

```
blade/
  .gitignore                (no repo yet; kept current)
  Blade.slnx
  docs/                     PROJECT_PLAN.md, DEMO_SCRIPT.md, architecture images
  src/
    Blade.Core/             Shared models, DTOs, contracts (filters, citations, audit records)
    Blade.Server/           ASP.NET Core minimal API: AI orchestration, retrieval, audit, RBAC stub
    Blade.Desktop/          WPF client (tabs, WebView2 graph, concept pages)
    Blade.DataGen/          Console app: generates SQLite DB + narratives + policy docs + embeddings
  data/
    blade.db                Generated (git-ignored, reproducible from seed)
    policies/*.md           Fictional policy manual
    legacy/*.png            Fake "scanned" reports for the OCR concept tab
  tests/
    Blade.Server.Tests/     Golden-question tests (do the demo questions return the planted cases?)
```

Config (`appsettings.json`): `AI:Provider = Ollama | OpenAICompatible`, model names, endpoint. The UI header shows the active provider with a green **LOCAL** / amber **CLOUD (POC ONLY)** badge.

---

## 8. Two-day build plan

**Day 1: Foundation + core AI**
1. ~~Install Ollama, pull models, benchmark speed on this laptop~~ **Done 2026-11-14** (see §2.1)
2. Scaffold solution ✅ (projects exist; server + WPF shell still template-only), WPF shell with tabs + provider badge
3. ~~DataGen: schema, synthetic data, planted storylines, policy manual, embeddings~~ **Done 2026-11-14** (see Progress log)
4. ~~Ask BLADE end-to-end~~ **Server done 2026-11-14** (UI pending)
5. ~~Policy Assistant end-to-end~~ **Server done** (UI pending)
6. ~~Audit middleware~~ **Server done**; Audit Log tab UI pending

**Day 2: Wow + polish**
7. Link Explorer (graph API + Cytoscape in WebView2 + narrated path)
8. Report Assistant (draft + quality check)
9. Home tab + concept tabs content
10. Golden-question tests; response cache for the demo questions (insurance against live failure)
11. Demo script, rehearsal, backup screen recording

### Progress log
- **2026-11-14: Data generator complete.**
  - `Blade.DataGen` builds `data/blade.db` in about 20–40 s:
    - 1,470 incidents, 4,074 CAD calls, 304 field interviews, 1,606 LPR reads, ~500 bookings, 195 pawn/scrap records
    - 18 policies split into 195 sections
    - 1,665 embeddings and FTS5 keyword indexes
  - Same seed gives the same data. Run with:
    ```
    dotnet run --project src/Blade.DataGen [-- --no-embed]
    ```
  - 19 tests in `tests/Blade.Tests` verify volumes, hygiene (fictional 555-01xx phones only, no juveniles in the county jail), determinism and every planted storyline link.
  - Planted storylines: S1 North burglary series (van → owner → co-resident → prior co-arrest → pawned serials), S2 catalytic ring (one phone shared by two identities), S3 escalating DV address, S4 knife/non-compliant use-of-force cases.
- **Retrieval findings** (reproduce with `dotnet run tools/retrieval-check.cs`):
  - Policy search is strong. "knife threat + non-compliant" → 300.3.6, then 300.3.5. "AI for reports" → 830.6, then 602.8.
  - Embedding raw narratives gave weak incident similarity, because every report shares the same dispatch opening and admin closing. **Stripping boilerplate before embedding** fixed it: the van query now puts 6 of 8 planted cases in the top 8.
  - For "show similar X", **re-ranking against the question is the wrong signal.** Ranking the filtered results by **mutual similarity** (how alike each case is to its nearest neighbours in the set) puts 6 of the 7 series cases on top. → Ask BLADE should detect clusters inside the filtered set and say "possible series of N", backed by shared entities (vehicle, serials, M.O.).
  - Use **hybrid retrieval**: structured filter → FTS5/BM25 keyword match + vector similarity → cluster detection.

- **2026-11-14: Server complete** (`src/Blade.Server`, http://localhost:5179). Run with:
  ```
  dotnet run --project src/Blade.Server --no-launch-profile
  ```
  Rehearse with `.\tools\golden-questions.ps1`. 35 tests pass (`dotnet test`).

  | Endpoint | Purpose |
  |---|---|
  | `GET /api/system` | Provider/model, LOCAL vs CLOUD, warm status, data counts (header badge) |
  | `POST /api/ask` | Ask BLADE: plan → hybrid search → link analysis → series → cited answer → guards → audit |
  | `POST /api/policy` | Policy Assistant: hybrid retrieval over 195 sections + related cases → cited answer |
  | `GET /api/incidents/{case}` | Full report (role-checked, audited as RECORD_VIEW) |
  | `GET /api/policy/sections[/{n}]` | Policy text for citation click-through |
  | `GET /api/audit`, `/audit/usage`, `/audit/verify` | Audit trail, per-user usage, hash-chain verification (SUPERVISOR/AUDITOR only; denials are logged) |

  Identity is a POC stub: `X-Blade-User`, `X-Blade-Role` and `X-Blade-Workstation` headers set by the desktop role switcher.

  **Live results (local qwen3:8b on the RTX 4060):**
  - Burglary golden question in about 22 s. It finds **exactly the 7 series burglaries**:
    - 4 entity-linked: the van seen by LPR near 3 scenes, and Cody Mercer's pawned serials matching 2 of them.
    - 3 added on M.O. and narrative similarity.
    - Travis Kessler is surfaced as the **bridge**: he lives with the van's registered owner and was co-arrested with Mercer.
    - All citations verified.
  - Policy answers in 5–8 s with correct sections (knife → 300.3.6, AI drafting → 830.6/602.x, stolen-car pursuit → 314.3).
  - A deputy's broad search: "10 records withheld for your role". Audit chain verifies, and deputy access to the audit log is denied (403) and logged.

  **Engineering lessons (talking points):**
  1. On an 8 GB GPU the chat and embedding models evicted each other (3–6 s reloads). Query embeddings now run on CPU in about 50 ms (`EmbeddingOnCpu`). A server-class GPU removes this.
  2. The 8B model sometimes omits citations in long prompts. Fixes: a cited example sentence, a reminder at the end of the prompt, and an **automatic regenerate if fewer than 2 verified citations**.
  3. The citation guard verifies *provenance* (the ID was in the model's context), not that each claim is supported. Next step for production: claim-level verification (an NLI check or a second-model judge).
  4. Most of the latency is answer generation (~18 s for ~750 tokens). **Streaming tokens to the UI** is the next UX improvement.

- **2026-11-14: Desktop app complete** (`src/Blade.Desktop`, WPF, dark theme). Verified end to end against the live server with UI Automation screenshots.

  | Tab | What works |
  |---|---|
  | Header | LOCAL AI / CLOUD badge, synthetic-data banner, role switcher |
  | Status bar | Model, warm state and data counts; a "Start local server" button when disconnected |
  | Security & Governance | 29 controls, each badged *Implemented / Stubbed / Production design* |
  | Architecture & Deployment | Data-flow diagram, production topology, integration patterns, model lifecycle, hardware sizing tiers |
  | Ask BLADE | Sample questions; "How BLADE read your question" chips; **filter editor** (re-run without the AI planner); cited answer with clickable case numbers; series card; connections with evidence; role-filtered results grid (series ★); Under the Hood panel |
  | Policy Assistant | Cited answer with clickable sections, fused section list with relevance, related historical cases, Under the Hood |
  | Link Explorer | Native force-directed graph (no web view or CDN). Search a plate, person, phone, case or address; click to inspect, double-click to expand; opens directly from an Ask answer with a "Possible series" hub |
  | Audit Log | Entries with row details and hashes, usage by user, hash-chain verification; deputies see "Access denied … logged" |
  | Opportunities | ROI summary plus 10 opportunity pages (problem, AI approach, local stack, data, integration, ROI formulas, risks, effort) |
  | Record viewer | Full synthetic report with persons, vehicles and property. Each view is audited as RECORD_VIEW |

  Server addition: `GET /api/graph?q=` (entity neighborhood, role-filtered, audited as GRAPH_QUERY) and `AskResponse.Graph`.
- **Demo script complete (2026-11-14):** `docs/DEMO_SCRIPT.md` has pre-flight checklists, a 20-minute run of show with click paths and talking points, Q&A prep for IT staff, a recovery playbook, a 12-minute cut and a cheat sheet. Its facts were checked against a live golden-question run: the burglary question took 22 s warm and about 50 s cold, because models unload after 60 idle minutes, so pre-flight warms them. The deputy-withholding demo question is "Vandalism and shoplifting incidents in the last 12 months": a deputy sees 220 incidents with 10 withheld, and an analyst sees 227 with 3 withheld.
- **Answer streaming complete (2026-11-14):** `POST /api/ask/stream` and `/api/policy/stream` return newline-delimited JSON events (`StreamEvent<T>`: context, delta, reset, done, error). The server sends the context event as soon as retrieval, link analysis and series detection finish, so results, the series and connections appear before the model writes. Answer text is released in whole words with PII redaction applied (`StreamingRedactor`), so a licence or SSN can never appear partially typed. Citations render as plain highlighted text until the final guard-checked answer replaces the stream. A citation-check regeneration sends a reset event. Audit entries are identical to the non-streaming endpoints, which remain for scripts. Measured warm: the burglary question shows results at 5.5 s and first words at 7.8 s, and finishes at 21.6 s, where it previously showed nothing until the end. The policy answer starts at 1.6 s. There are 7 new tests, 42 in total.
- **One-click launcher (2026-11-14):** `Start-BLADE.cmd` runs `tools/start-demo.ps1`. The script checks power, the .NET SDK, Ollama (starting it if needed) and both models. It builds, generating data if it's missing, then starts the server hidden with a log in `logs/`, waits for warm models and opens the app. It re-warms the models every 20 minutes while the app is open, and stops the server when the app closes. If BLADE is already running, it reuses the server and brings the app to the front. `Stop-BLADE.cmd` cleans up. Tested from a cold start (about 1 minute to ready), on a second launch while running, on app close (no processes left) and with the stop script. The script is kept ASCII-only for Windows PowerShell 5.1.
- **Data Readiness page (2026-11-15):** a new PLATFORM page for the "how would we use our database or API?" question. It covers the six steps for bringing a source online, for both existing records (backfill) and new records (ongoing); a source prioritization table with waves; the phases (foundation, then waves 1-3); effort and skills by kind of source; the "ready enough" checks; a SQL Server reference pattern (side tables in their own database, one shared Chunk table with `embed_model`, shared versus per-source comparison, version note); and 17 short answers, including embedding versus chat models and testing several models. An embedding-model guide (2026-11-15) rates bge-m3 (top candidate for police records), qwen3-embedding (highest accuracy), nomic-embed-text (baseline, used in BLADE), granite-embedding (resource-constrained), embeddinggemma and mxbai-embed-large on five dimensions with 1-5 dots, and suggests a three-model test. The ratings come from published model information, not measurements on BLADE data. All text is in `content.json` under `dataReadiness`. Also fixed the tables swallowing the mouse wheel, so pages scroll past them (`ScrollBehavior.BubbleWheel`); the Architecture page had the same problem. Effort figures are planning ranges, labeled as such on the page.
- **Remaining ideas** (not started): a live Report Narrative Assistant / Quality Checker tab; a backup screen recording, made during rehearsal.

**Cut list if time runs short (in order):** Report Assistant → graph narration → concept tab visuals → role switcher.

---

## 9. Demo risk mitigation
- **Golden questions** rehearsed and tested; answers cached as a "demo safety net" (disclosed if used).
- **Warm the models** before presenting (first load takes ~20 s from disk cache, up to 75 s right after a reboot). The server will warm both models on startup and set `keep_alive = -1`.
- **Backup video** of the full flow.
- **Offline proof:** turn Wi-Fi off before starting the live tabs.
- Close Docker/VS during the demo to free RAM and VRAM.

---

## 10. Decisions (2026-11-14)

| Topic | Decision | Impact on the build |
|---|---|---|
| Agency identity | **Fictional: "Kestrel County Sheriff's Office"** (KCSO). Districts North/South/East/West; invented streets, people, businesses | Watermark everywhere: *SYNTHETIC DATA: FICTIONAL AGENCY* |
| Audience | **IT / technical staff** | Lead with architecture, security and integration. Add the **"Under the Hood" panel** and **Architecture & Deployment tab** below. Concept tabs keep ROI, but each also gets a "how it integrates" section |
| UI framework | **WPF on .NET 10** (Fluent theme) | As planned |
| Demo shape | **Presenter-driven live demo**, ~15–20 min | Scripted golden questions + `docs/DEMO_SCRIPT.md`; synthetic data only needs to be deep around the storylines |
| Their RMS/CAD/JMS vendors | **Unknown, keep generic** | Integration diagrams say "your RMS / CAD / JMS (read-only replica or vendor API)" |
| Name | **BLADE: Bureau Linked Analytics & Data Explorer** | App title bar + splash |
| Source control | **No git repo yet.** Maintain `.gitignore` only; the user will initialize the repo once there is a first testable version | Keep `.gitignore` current as projects/artifacts are added |

### Additions driven by an IT audience
- **"Under the Hood" side panel** on every live AI tab (collapsible): the exact system prompt, interpreted filter JSON, retrieved records/chunks with similarity scores, the final prompt size, model name/version, provider (LOCAL/CLOUD), latency per stage (interpret → retrieve → generate), token counts. This shows we hide nothing and know where every millisecond goes.
- **Architecture & Deployment tab** (static, rich):
  - Production topology: model server on a segmented VLAN, API gateway, read-only replicas of RMS/CAD/JMS, nightly + near-real-time ingestion, vector/graph store, SIEM forwarding of audit events.
  - **Hardware sizing**: this laptop (8 GB GPU) vs a departmental server (e.g., 1–2 × 48 GB workstation/data-center GPUs serving larger models to ~50–150 concurrent users) vs a regional/shared deployment. Present as ranges, not quotes.
  - Integration patterns: database replica / CDC, vendor APIs, file drops for legacy exports, OCR pipeline for paper.
  - Model lifecycle: approved-model registry, evaluation suite, rollback.
- **Evaluation harness** (golden questions → expected report IDs and policy sections) shown as a small pass/fail table. IT staff will ask "how do you know it's right?"
