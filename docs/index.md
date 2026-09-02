---
layout: home

hero:
  name: "Klexir.Workflow"
  text: "Durable workflow & saga orchestration"
  tagline: A fluent, type-checked step builder plus an engine that runs it, checkpoints it, and can resume it after a restart — built on MonadicSharp Result<T>.
  actions:
    - theme: brand
      text: Quick example
      link: /guide
    - theme: alt
      text: Full README on GitHub
      link: https://github.com/Danny4897/Klexir.Workflow
    - theme: alt
      text: Klexir Ecosystem
      link: https://danny4897.github.io/MonadicSharp/ecosystem

features:
  - title: Compensating sagas
    details: Steps pair with Compensate — a failure mid-saga unwinds what already ran, in reverse order, before marking the instance Failed.
  - title: Crash-resumable
    details: FileWorkflowStore checkpoints every step; a fresh WorkflowEngine can pick a running instance back up after a restart.
  - title: Part of the Klexir Ecosystem
    details: One of 7 experimental .NET repos exploring systems-programming concepts — see the full ecosystem on MonadicSharp's docs.
---
