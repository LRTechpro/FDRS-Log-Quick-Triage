# FDRS Log Quick Triage

A Windows Forms (.NET) desktop utility for rapidly reviewing Ford Diagnostic & Repair System style log files by extracting high-signal entries, classifying severity, filtering results in real time, and exporting findings to CSV.

This is an independent portfolio project. It is not an official Ford tool and does not reproduce Ford internal processes or certify a root cause or cybersecurity condition.

## Project Overview

Diagnostic logs can contain thousands of lines, many of which are not relevant during initial triage. This tool focuses on speed, clarity, and decision support by:

* Surfacing high-value diagnostic lines
* Automatically classifying severity
* Allowing rapid filtering without modifying raw data
* Exporting evidence suitable for documentation or escalation

## Engineering Context and Operational Relevance

In automotive, cybersecurity, and embedded systems environments, engineers must identify faults quickly, reduce cognitive load, and justify decisions with evidence.

This project demonstrates how to:

* Extract signal from noisy diagnostic data
* Preserve raw data integrity while enabling fast analysis
* Build user-interface utilities that support engineering decision making
* Apply explainable, auditable classification logic

The same patterns can support ECU diagnostics, telemetry review, incident triage, and other evidence-driven engineering workflows.

## Technical Highlights

### Event-Driven WinForms Architecture

The application uses explicit event wiring and a controlled rendering pipeline.

### Single Render Pipeline

All user-interface updates flow through `ApplyFilter`, ensuring consistent state and predictable behavior.

### Immutable Master Dataset

Extracted log entries are stored once. Filters operate on views of the data rather than mutating the original collection.

### Explainable Severity Classification

Severity classification uses transparent keyword-based rules that can be audited and adjusted.

### Efficient Deduplication

Duplicate log messages are removed with a case-insensitive `HashSet`, providing efficient lookup while collapsing repeated faults into distinct failure modes.

## Core Functionality

* Load `.txt` diagnostic log files
* Extract relevant lines using keyword-based regular expressions
* Classify entries as Error, Warning, or Info
* Filter by text and severity
* Display unique-only results
* Export currently visible rows to CSV
* Keep the results grid read-only to preserve source integrity

## Severity Classification Logic

| Severity | Matching Keywords |
|---|---|
| Error | `error`, `fail`, `nrc`, `denied` |
| Warning | `warning`, `timeout`, `voltage` |
| Info | Default fallback |

These rules are intentionally simple, transparent, and suitable for controlled testing with synthetic or sanitized diagnostic data.

## CSV Export

The application exports only the currently visible rows and honors active search, severity, and uniqueness filters.

```csv
Line,Severity,Message
248,Error,"Voltage out of range detected"
```

## Data Handling Boundary

This public repository must contain only synthetic or fully sanitized data.

Do not commit:

* Real Ford or other OEM diagnostic logs
* Vehicle Identification Numbers
* Customer, technician, or dealer information
* Internal endpoints, software records, tickets, or proprietary documentation
* ECU firmware, calibration files, credentials, or seed-key material

User-selected logs are read locally by the application. Review and sanitize every screenshot before publishing it.

## User Interface

### Application Startup

![Initial App](screenshots/1_fdrs_lqt_initial_app.png)

### Parsed Diagnostic Log

![Parsed Log](screenshots/2_fdrs_lqt_parsed_log.png)

### Information Filter

![Info Filter](screenshots/4_fdrs_lqt_filter_info.png)

### Security and Access Filter

![Security Filter](screenshots/3_fdrs_lqt_filter_security.png)

### Unique Error View

![Unique Errors](screenshots/5_fdrs_lqt_unique_errors.png)

### CSV Export

![CSV Export](screenshots/6_fdrs_lqt_csv_export.png)

## How to Run

1. Open the solution in Visual Studio.
2. Build the project.
3. Run the application.
4. Select a synthetic or sanitized log with **Open Log**.
5. Apply filters as needed.
6. Export the visible findings to CSV.

## Portfolio Positioning

This repository demonstrates C#, .NET WinForms, automotive diagnostic-domain knowledge, explainable log triage, immutable data handling, filtering, deduplication, and evidence export. It should be evaluated as a portfolio prototype rather than a production or enterprise diagnostic platform.

## License

MIT License

## Author

**Harold L. R. Watkins**

Software Verification & Infrastructure Analyst, Ford Motor Company through TEKsystems

Automotive Cybersecurity | Embedded Systems | Vehicle Software Verification
