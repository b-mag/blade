# BLADE — Bureau Linked Analytics & Data Explorer

Proof of concept: a local-AI investigative and policy assistant for a sheriff's office. It runs against **synthetic data for the fictional Kestrel County Sheriff's Office**. No real CJI is used.

<img width="1520" height="802" alt="Screenshot 2026-11-14 161030" src="https://github.com/user-attachments/assets/044138be-d553-4538-b20b-6e1d02ea9b0b" />


| Project | What it is |
|---|---|
| `src/Blade.DataGen` | Generates `data/blade.db`: RMS/CAD/FI/LPR/JMS/pawn data with planted investigative storylines, the policy manual, embeddings and FTS indexes |
| `src/Blade.Server` | ASP.NET Core API on http://localhost:5179. Handles role filtering, hybrid retrieval, the entity graph, grounded LLM answers, the citation guard and the hash-chained audit log |
| `src/Blade.Desktop` | WPF client: Security & Governance, Architecture, Ask BLADE, Policy Assistant, Link Explorer, Audit Log, and 10 opportunity pages |
| `src/Blade.Core` | Shared contracts and vocabulary |
| `tests/Blade.Tests` | 42 tests covering data integrity, planner, guards, audit tamper detection, RBAC, graph links and streaming/PII safety |

## Prerequisites
- .NET 10 SDK
- [Ollama](https://ollama.com) with the two models:
  ```
  ollama pull qwen3:8b
  ollama pull nomic-embed-text
  ```

## Demo day: one double-click
Double-click **`Start-BLADE.cmd`**. It checks Ollama and the models, builds, generates the data if it is missing, starts the server, waits for the models to warm up, and opens the app. Its window minimizes and keeps the models warm. Closing the app stops the server. Server logs go to `logs\`.

`Stop-BLADE.cmd` stops anything left running. To skip the build, run:
```
Start-BLADE.cmd -NoBuild
```

## Run manually
Generate the data once (~40 s with embeddings):
```
dotnet run --project src/Blade.DataGen
```

Start the server. It warms the models on startup:
```
dotnet run --project src/Blade.Server --no-launch-profile
```

Start the desktop app:
```
dotnet run --project src/Blade.Desktop
```

The desktop also has a **Start local server** button in its status bar when the server isn't running.

## Rehearse and verify
Run the golden demo questions against the running server:
```
.\tools\golden-questions.ps1
```

Run the tests:
```
dotnet test
```

Check retrieval quality:
```
dotnet run tools/retrieval-check.cs
```

## Demo tips
- Models unload after 60 idle minutes. About 10 minutes before presenting, warm them (`Invoke-RestMethod -Method Post http://localhost:5179/api/admin/warmup`) and ask one question.
- Use the role switcher (DEPUTY / ANALYST / SUPERVISOR / AUDITOR) to show record withholding and audit-log access control.
- Turn Wi‑Fi off before the live tabs. Everything keeps working.

See `docs/DEMO_SCRIPT.md` for the timed demo walkthrough, pre-flight checklist and Q&A prep.

See `docs/PROJECT_PLAN.md` for the full plan, decisions, benchmark results and engineering notes.
