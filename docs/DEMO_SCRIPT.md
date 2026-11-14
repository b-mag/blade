# BLADE demo script

**Audience:** sheriff's office IT and technical staff.
**Format:** presenter-driven live demo.
**Main run:** 20 minutes, plus Q&A. A 12-minute cut is near the end.

Three things the audience should leave with:
1. **Value.** Local AI finds connections across systems that a person would have to piece together by hand. It also answers policy questions with the section numbers attached.
2. **Trust.** It is on-prem, role-filtered, cited, audited and tamper-evident, and you can see exactly what the model was given.
3. **ROI.** A short, ranked list of next uses, each with a formula they can fill in with their own numbers.

> Times below are cumulative. The **SAY** lines are talking points, not a script to read. **CLICK** is the exact path in the app.
> LLM wording changes slightly between runs. The case numbers, the series, the links and the counts are computed by code and do not change.

---

## Pre-flight

### The day before
- [ ] Run a full rehearsal with a timer, then record it as a backup video. Use Win+Alt+R (Xbox Game Bar), or OBS.
- [ ] Rehearse clicking **Verify hash chain** in the app. This button has only been checked through the API.
- [ ] Turn off Windows notifications (Focus / Do Not Disturb) and set the laptop to never sleep while on power.
- [ ] Set the display to the projector resolution and check the app at that size. The app opens maximized.

### 60 minutes before
1. Plug in power. A GPU on battery is much slower.
2. Make sure Ollama is running. The tray icon should be visible, and `ollama ps` should respond.
3. Start the server from the `blade` folder:
   ```bash
   dotnet run --project src/Blade.Server --no-launch-profile
   ```
4. Start the desktop:
   ```bash
   dotnet run --project src/Blade.Desktop
   ```
5. Run the golden questions. Every ASK section should show the 7-case series, and the security checks should say `OK`:
   ```powershell
   .\tools\golden-questions.ps1
   ```

### 10 minutes before
Models unload after 60 idle minutes. On a cold first question, the results take about 25 s to appear instead of about 5 s.
1. Warm the models:
   ```powershell
   Invoke-RestMethod -Method Post http://localhost:5179/api/admin/warmup
   ```
2. In the app, open **Ask BLADE** and run the first sample once. Leave its answer on screen. You will run it again live.
3. Set the role to **ANALYST** (user `j.alvarez`).
4. Go to **Security & Governance**, scrolled to the top.
5. Leave Wi-Fi **on** for now. You will turn it off on stage.
6. Close everything else on the laptop: email, chat, browsers.

---

## Run of show

| Time | Segment | Screen |
|---|---|---|
| 0:00 | 1. Opening and framing | Security & Governance |
| 1:30 | 2. Security and architecture tour | Security & Governance, Architecture |
| 4:30 | 3. Go offline | Header badge |
| 5:00 | 4. Ask BLADE: the burglary series | Ask BLADE |
| 10:00 | 5. Link Explorer | Link Explorer |
| 12:00 | 6. Policy Assistant | Policy Assistant |
| 14:30 | 7. Roles, withholding and audit | Ask BLADE, Audit Log |
| 17:30 | 8. Opportunities and ROI | ROI Summary, 2 opportunity pages |
| 19:30 | 9. Close | ROI Summary |
| 20:00 | Q&A | Any page |

If you have a 30-minute slot, spend the extra time on Under the Hood, the optional Jaylen Ortega graph, and re-running the withholding question as ANALYST.

---

### 1. Opening and framing (0:00 – 1:30)
**Screen:** Security & Governance.

**SAY:**
- "This is BLADE, the Bureau Linked Analytics & Data Explorer. It's a working proof of concept, built in two days, of what local AI looks like against sheriff's office data."
- Point at the red banner: "Everything you'll see runs on **synthetic data**. Kestrel County is fictional, and so is every name, plate, phone and case. No real CJI touched this laptop."
- "I'm going to show you the system, but I'll spend as much time on **how it's controlled** as on what it does. That is the part you'd have to support."
- "Three questions for today: is it useful, can it be trusted and audited, and where's the return?"

### 2. Security and architecture tour (1:30 – 4:30)
**CLICK:** Security & Governance. Scroll slowly; don't read it aloud.

**SAY:**
- "Every control here carries one of three labels: **Implemented in POC**, **Stubbed in POC**, or **Production design**. I'm not going to claim something is done when it isn't."
- Pick three to call out:
  - **Role-based retrieval filtering before the model.** "The model never sees a record the user isn't allowed to see. Filtering happens in the database query, not in the prompt."
  - **Append-only, hash-chained audit log.** "Every question, every record view, every denial. I'll prove it's tamper-evident later."
  - **Model output is never executed.** "The AI reads your question into a structured filter that is checked against a fixed list of values. Code runs parameterized SQL. The model never writes a query."
- Be upfront about the stubs: "Identity is a role switcher in this POC. In production it's AD or Entra ID with MFA."

**CLICK:** Architecture & Deployment.

**SAY:**
- "Thin clients talk to an API tier. The model server sits on its own segmented VLAN with no outbound internet. It reads a read-only replica, not your production RMS."
- Point at the sizing tiers: "This laptop has an 8 GB GPU. The tiers show what a department pilot and an agency-wide rollout would need."

### 3. Go offline (4:30 – 5:00)
**DO:** Turn **Wi-Fi off** in view of the audience.

**SAY:**
- Point at the green **LOCAL AI  qwen3:8b** badge in the header. "That badge tells you where inference is happening. Right now it's this laptop, using an open-weight model. No cloud, no per-question fees, and nothing leaves the building."
- "There's a cloud switch for prototyping on fake data. When it's on, the badge turns amber and reads **CLOUD (POC ONLY)**, so it can't be on quietly."

### 4. Ask BLADE: the burglary series (5:00 – 10:00)
**CLICK:** Ask BLADE. Click the first sample: *Show similar burglaries in the north district last 18 months involving forced entry on commercial buildings after 10pm*. Click **Ask**.

**What happens:** after about 5 s the filter chips, results grid, series card and connections appear. The answer starts typing at about 8 s and finishes at about 22 s. Case numbers in the answer become clickable links once it finishes.

**While the first 5 s run, SAY:**
- "This is the question an investigator actually asks. It's a pattern question across the RMS, plus whatever else we know."

**While the answer types, SAY:**
- "Notice the results, the series and the connections are already on screen. Those are computed by code from six systems: RMS incidents, CAD, field interviews, license plate readers, jail bookings and pawn records. The AI is now writing a summary of what code already found."
- "The case numbers turn into links when it finishes. That's when the citation check has run, so nothing is clickable before it's been verified."

**When the answer finishes, walk through it top to bottom:**
1. **Filter chips.** "First, this is how the AI interpreted the question: burglary, North district, commercial, forced entry, the last 18 months, 22:00 to 06:00. You can check it, and if it's wrong, you fix the filter and click **Re-run with my filter**, which skips the AI planner."
2. **Answer.** "Every claim is cited." Click a case number, for example **26-001925**, to open the full report. Close it.
3. **Possible series card.** "It found 7 incidents from August 2025 to September 2026. Four are tied together by shared entities. Three were added because they match the M.O. and read alike." Then: "Note the wording: *Requires analyst confirmation.* It's a lead, not a conclusion."
4. **Connections.** This is the payoff, so slow down:
   - "The white 2016 Ford Transit, plate **418KTV**, was caught on plate readers near three of the scenes, within the hour."
   - "**Cody Ray Mercer** pawned items whose serial numbers match property stolen in two of these burglaries."
   - "**Travis Dean Kessler** is the bridge. He lives with the van's registered owner and was co-arrested with Mercer."
   - "Nobody typed those names. They come from four different systems, and no single system holds the whole picture."
5. If someone asks about the **Heritage Gas Station** location link: "Two incidents at the same address. The system shows it, but it's labeled a location link and kept out of the series. Coincidences get surfaced; a person decides what they mean."

**CLICK:** **Under the Hood**.

**SAY:**
- "This is for you. It shows the exact prompts, the timing for each stage, the records the model was given, and the safety checks."
- Point at the stages: "Interpret, embed, retrieve, link, series, compose, citation check. Retrieval is under 200 ms. Almost all the time is the model writing."
- Point at the citation check: "Every case, FI, booking and pawn number in the answer is checked against what the model was actually given. If it cites something it wasn't given, the citation shows as **unverified** in red. If there are too few verified citations, it regenerates the answer."

### 5. Link Explorer (10:00 – 12:00)
**CLICK:** Back on the answer, click **Open in Link Explorer**.

**SAY and DO:**
- "These are the same connections as a graph. The gold node is the possible series, with its seven incidents around it."
- Drag a node or two so the layout reads well.
- Click the **418KTV** van node, and point at its details.
- Double-click **Cody Ray Mercer** to expand. "Pawn tickets, his booking, the field interview where he was stopped with the van."
- Optional, if time allows: click the **Jaylen Ortega** sample. "A different case: catalytic converter thefts, and a suspect using an alias. The alias and the real name connect through a shared phone number."

**SAY:** "Victims and reporting parties are deliberately excluded from link analysis, so the system doesn't connect a burglary victim to a suspect just because they share an address."

### 6. Policy Assistant (12:00 – 14:30)
**CLICK:** Policy Assistant. Click the first sample: *What does policy say about use of force with a knife threat when the subject is non-compliant?* Click **Ask policy**. The sections list appears at once, the answer starts within about 2 s and finishes in about 7 s.

**SAY:**
- "A deputy or supervisor asks in plain English. The answer cites the exact sections: 300.3.6 on edged weapons, 300.4.1 on deadly force criteria, and 300.3.4."
- Click **Policy 300.3.6**: "One click shows the actual text. Trust, but verify."
- Point at the sections list: "Here are the ranked sections it found, and below them, related past incidents from the RMS."
- Point at the footer: "Policy reference only, not legal advice. That disclaimer comes from code, not from the model."

**CLICK:** The second sample: *Can I use AI to help write my report narrative?*

**SAY:**
- "We wrote an AI-use policy into the sample manual, Policy 830. It says yes, but the deputy stays the author, has to review everything, has to write their own force justification, and has to add a disclosure."
- "Hold that thought. It's the number-one item on the ROI list."

### 7. Roles, withholding and audit (14:30 – 17:30)
**CLICK:** Switch the role to **DEPUTY** (user `dep.harmon`). Go to **Ask BLADE** and type:
*Vandalism and shoplifting incidents in the last 12 months*
Click **Ask**. The count appears in about 2.5 s; you don't need to wait for the answer to finish. If the summary line shows an **UNVERIFIED** citation, that's the guard working: say so and move on.

**SAY:**
- "Same system, logged in as a deputy. The summary line reads **220 matching incidents · 10 withheld for your role**. Those involve juveniles, and a deputy's role can't see them."
- "The deputy isn't misled into thinking there's nothing more there. They know to ask someone with access."
- "An analyst sees juvenile records, so their count is 227, with 3 restricted records still withheld. A supervisor sees everything." Don't re-run this unless asked.

**CLICK:** Still as DEPUTY, open **Audit Log**.

**SAY:** "Access denied, and this attempt has been logged."

**CLICK:** Switch to **SUPERVISOR** (user `sgt.whitaker`). Open **Audit Log** again.

**SAY and DO:**
- Point at the top entries: "There's the deputy's denied attempt, a few seconds ago."
- Point at the usage table: "Usage per user: AI queries, record views, denials and tokens. This is how you'd notice someone fishing."
- Click **Verify hash chain**: "Every entry includes a hash of the one before it. Change or delete one row and verification fails at that row. There's an automated test that does exactly that."
- "In production, this forwards to your SIEM and sits on write-once storage."

### 8. Opportunities and ROI (17:30 – 19:30)
**CLICK:** ROI Summary.

**SAY:**
- "Ten opportunities, ranked by time returned against feasibility and risk. The important point: they all reuse the same model server, audit log and role filters, so the second use case costs less than the first."
- "There are no made-up statistics here. Each one is a formula, such as reports per year × minutes saved × loaded hourly cost. The assumptions are marked *replace with your numbers*."

**CLICK:** **Report Narrative Assistant** (rank 1).

**SAY:**
- "It drafts the narrative from fields the deputy has already entered, and the deputy stays the author. That's exactly what Policy 830 allowed."
- Point at the effort: "Pilot in 8–12 weeks with one patrol shift and two or three report types."

**CLICK:** **Public Records Request Redaction Assistant** (rank 2).

**SAY:** "It proposes redactions and a records clerk approves every one. It goes after the backlog and the deadline risk."

Mention the rest in one breath: body-cam transcription, a report quality checker, legacy data resurrection (OCR on old paper and exports), jail intelligence, and dispatch assist.

### 9. Close (19:30 – 20:00)
**SAY:**
- "So, to recap. **Value**: it connected a van, a pawn seller and a co-arrestee across six systems in about 5 seconds. **Trust**: local, role-filtered, cited, audited and tamper-evident. **ROI**: a ranked list with formulas you can fill in."
- "A sensible next step is to pick one use case, measure a two-to-four-week baseline, then run a small pilot against a read-only replica and measure the same things."
- Turn Wi-Fi back on after the demo, not before Q&A ends.

---

## Q&A prep (likely IT questions)

| Question | Answer |
|---|---|
| **What hardware does this need?** | Here, a laptop with an 8 GB GPU running an 8B-parameter open-weight model. For a department, one GPU server serves a team. The Architecture page has the tiers. |
| **Why not Copilot or ChatGPT?** | Data leaves the building, and that is a CJIS problem. With local open-weight models there are no per-token fees, and you control versions and updates. |
| **Is this CJIS compliant?** | The POC doesn't claim compliance. It shows the shape of the controls: audit, role filtering and on-prem inference. Production needs advanced authentication, encryption at rest and in transit, personnel screening and hosting controls, all listed under Production design. |
| **How do you stop hallucination?** | Answers are grounded in retrieved records. The citation guard flags any identifier the model wasn't given, and the answer regenerates if there are too few citations. *Be honest:* the guard checks that a citation is real, not that it supports the claim. Claim-level checking is on the production list. Links and series are computed by code, not by the model. |
| **What about prompt injection?** | The model's output is never executed. The interpreted filter is schema-constrained to fixed values and becomes parameterized SQL. Narratives are treated as data. In production, an injected narrative could still skew a summary's wording, which is one reason a person reviews it and the citations are clickable. |
| **How does it connect to our RMS, CAD and jail system?** | Read-only: a database replica or change data capture, vendor APIs, or file drops for legacy exports. Nightly loads plus near-real-time feeds. It never writes back. |
| **How do you know it's accurate?** | The synthetic data has planted storylines with a hidden answer key. The server never reads it; the tests do. There are 35 automated tests and a golden-question script that runs before every demo. In production, the same idea becomes your own evaluation set. |
| **What happens when the model is updated?** | An approved-model registry, pinned versions, a golden-question evaluation before promotion, and rollback. |
| **How is the search done?** | It's hybrid: a SQL filter, then keyword search (SQLite FTS5 with BM25), plus vector similarity using nomic-embed-text. The two rankings are combined. The entity graph is built from structured links and from plates and phone numbers pulled out of narratives. |
| **Bias and civil liberties?** | Series and links are leads that require analyst confirmation. Victims and reporting parties are excluded from link analysis. Every query is audited. Production includes a bias and civil-liberties review and a public AI-use policy. |
| **How long did this take to build?** | Two days for the POC. That's the point: the pattern is reusable, and the hard part is governance and integration, not the AI. |
| **What's it written in?** | .NET 10: a WPF client, an ASP.NET Core API, SQLite, and Ollama for the models. |

---

## Recovery playbook

| Problem | What to do |
|---|---|
| The status bar says the server is offline | Click **Start local server** in the status bar. Wait about 15 s while it warms the models. |
| The first results take about 25 s to appear | The models were cold. Keep talking with the "while the first 5 s run" points. The next question is back to normal speed. |
| Ollama isn't responding | Start Ollama from the Start menu. Then run the warmup command from Pre-flight. |
| An answer comes back thin or uncited | Click **Ask** again. The server already retries once on its own when citations are missing. If an **unverified** citation appears, use it: "That's the guard catching the model." |
| The graph looks cluttered | Drag the gold series node to the center, or re-open it with **Open in Link Explorer**. |
| The burglary question is slow or fails twice | Use the catalytic sample instead. It takes about 13 s and shows a 3-case series linked to Jaylen Ortega. |
| Anything worse | Switch to the backup recording, and keep the Security & Governance and ROI pages live. Those don't need the model. |

---

## 12-minute cut
1. **Opening, 1 min.** Use the synthetic-data banner and the three questions.
2. **Go offline, 0:30.** Turn off Wi-Fi and point at the LOCAL badge.
3. **Ask BLADE, 4 min.** Run the burglary question. Show the series, the three connections and Under the Hood.
4. **Link Explorer, 1:30.** Use the "Open in Link Explorer" button only.
5. **Policy, 1:30.** The knife question only. Click one section.
6. **Audit, 2 min.** DEPUTY is denied the audit log; SUPERVISOR sees the denial and verifies the chain. Skip the withholding query and just say it.
7. **ROI and close, 1:30.** ROI Summary page only.

---

## Cheat sheet

**Burglary series (S1).** 7 North district commercial burglaries, Aug 2025 – Sep 2026:

| Case | Why it's in the series |
|---|---|
| 25-001921 | Same M.O. |
| 25-002136 | Van on LPR |
| 25-002305 | Same M.O. |
| 26-001306 | Van on LPR |
| 26-001620 | Mercer pawn |
| 26-001925 | Van on LPR + Mercer pawn |
| 26-002228 | Same M.O. |

**People and records in the burglary story:**

| Who / what | Details |
|---|---|
| Van | **418KTV**, white 2016 Ford Transit, registered to Amber Lynn Pruitt |
| Amber Lynn Pruitt | Lives with Travis Kessler at 2217 Quail Run Ct |
| Cody Ray Mercer | FI **FI-26-00264** (with the van); pawn tickets **PT-FP-10142** and **PT-FP-10162** at Frontier Pawn |
| Travis Dean Kessler | Co-arrested with Mercer on booking **B25-004159** |

**Other storylines:**
- **Catalytic converters (S2):** Jaylen Marcus Ortega, alias "Jay Ortiz", linked by a shared phone. FI-26-00251.
- **Domestic escalation (S3):** 1418 Larkspur Ct, Ashby Mills. Five calls ending in a knife threat. Try the Link Explorer sample "1418 Larkspur".

**Role and user names in the app:**

| Role | User | Sees |
|---|---|---|
| DEPUTY | dep.harmon | Normal records only |
| ANALYST | j.alvarez | Normal + juvenile |
| SUPERVISOR | sgt.whitaker | Everything |
| AUDITOR | audit.fenton | Audit log only |

**Typical timings (warm, this laptop):**

| Question | Results appear | Answer starts | Answer finishes |
|---|---|---|---|
| Burglary question | about 5 s | about 8 s | about 22 s |
| Catalytic question | about 4 s | about 5 s | 13–21 s |
| Policy questions | under 1 s | about 2 s | 6–8 s |
| Deputy withholding question | about 2.5 s | about 3 s | about 13 s |
