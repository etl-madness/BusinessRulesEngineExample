# 🎯 Parallel Workflow Implementation Guide: EtlAnalytics.RulesEngine Demo

> **Note**: `BusinessRulesEngineExample` is a demonstration application built to showcase how to integrate, configure, and execute workflows using the `EtlAnalytics.RulesEngine` and `EtlAnalytics.RulesEngine.Dapper` packages.

This guide outlines how parallel execution works natively in `EtlAnalytics.RulesEngine` and details the implementation plan to enable parallel workflow authoring and visualization in this demo application.

---

## 1. Core Architecture & Native Parallel Engine Model

`EtlAnalytics.RulesEngine` natively supports parallel rule execution within a `BusinessRuleBundle` through **Sequence Grouping**.

### 1.1 How Native Sequence Grouping Works
- **Sequence Order**: Each `BusinessRuleBundleItem` in a bundle has a `SequenceOrder` integer property.
- **Stage Execution**: The engine groups rules by `SequenceOrder` and executes stages sequentially in ascending order (Stage 1 $\rightarrow$ Stage 2 $\rightarrow$ Stage 3).
- **Parallel Group Execution**:
  - **Single Rule Group**: Executed sequentially using `await ExecuteRuleAsync(...)`.
  - **Multi-Rule Group**: Executed concurrently using `Task.WhenAll(...)`.
- **Result Aggregation**:
  - When a parallel stage executes, all rule outputs for that stage are aggregated into a `List<object?>`.
  - The aggregated list is stored in `context.PreviousResult` for the next stage.
  - Individual stage results are accessible via `context.StepResults[sequenceOrder]`.
- **Exception Handling & Fail-Fast**:
  - If any rule in a parallel stage throws an unhandled exception, `Task.WhenAll` fails, a `[FATAL]` error is logged, and execution terminates immediately.

---

## 2. Demo Application Goals

As a **demo project**, `BusinessRulesEngineExample` should demonstrate the engine's built-in parallel features clearly and idiomatically without adding complex custom compiler layers or custom persistence tables.

### Key Objectives
1. **Stage-Based Parallel UI**: Update `Pages/Bundles.razor` to display and edit rules visually grouped into **Execution Stages** (`SequenceOrder`).
2. **Parallel Stage Management**: Allow users to add multiple rules to the same stage (for parallel execution) or create new sequential stages.
3. **Execution Visualization**: Highlight parallel execution steps during bundle runs and demonstrate how aggregated outputs (`List<object?>`) flow into subsequent C# or TSQL rules.
4. **API Execution Example**: Demonstrate triggering parallel bundles via HTTP API (`Controllers/RulesController.cs`).

---

## 3. UI & Feature Implementation Plan

### Phase 1: Bundle Authoring UI Enhancement (`Pages/Bundles.razor`)
- [x] **Group Items by Sequence Order**: Present bundle items grouped by `SequenceOrder` as distinct **Execution Stages**.
- [x] **Stage Controls**:
  - **Add Rule to Existing Stage**: Assign a new rule to an existing `SequenceOrder` to run it in parallel with other rules in that stage.
  - **Add New Stage**: Append a new stage with a higher `SequenceOrder`.
  - **Re-order / Move**: Allow moving rules between stages or adjusting stage execution order.
- [x] **Parallel Badge & Visual Indicators**: Clearly label multi-rule stages with a "Parallel Execution (Task.WhenAll)" badge in the UI.

### Phase 2: Parallel Result Inspection & Logging
- [x] **Log Viewer Formatting**: Ensure the execution log clearly indicates when a parallel stage begins and completes.
- [x] **Step Result Preview**: Display how downstream rules receive parallel outputs as `List<object?>` in `PreviousResult` or `StepResults`.

### Phase 3: Controller & API Demonstration (`Controllers/RulesController.cs`)
- [x] **Bundle API Execution**: Retain and document `POST /api/rules/bundle/execute/{bundleName}` to demonstrate programmatic execution of parallel bundles.

---

## 4. Code Touchpoints & Implementation Checklist

| Component | File Path | Scope of Changes |
| :--- | :--- | :--- |
| **Bundle UI** | [Bundles.razor](file:///C:/Users/U00001/source/repos/BusinessRulesEngineExample/Pages/Bundles.razor) | Group items by `SequenceOrder`, add stage management UI, show parallel badges. |
| **API Controller** | [RulesController.cs](file:///C:/Users/U00001/source/repos/BusinessRulesEngineExample/Controllers/RulesController.cs) | Update XML docs and response models to clarify parallel execution behavior. |
| **Tutorial Doc** | [tutorial.md](file:///C:/Users/U00001/source/repos/BusinessRulesEngineExample/docs/tutorial.md) | Add parallel bundle creation section and explain data passing in parallel stages. |
| **AI Guide** | [ai_implementation_guide.md](file:///C:/Users/U00001/source/repos/BusinessRulesEngineExample/docs/ai_implementation_guide.md) | Verify parallel execution references match engine behavior. |

---

## 5. Summary of Plan Steps

1. **Audit Bundle UI (`Pages/Bundles.razor`)**: Ensure rules sharing `SequenceOrder` are rendered as parallel groups.
2. **Add Stage Management Actions**: Provide intuitive buttons to add rules into existing parallel stages or create new stages.
3. **Verify Data Flow**: Ensure C# script rules consuming `globals.PreviousResult` from a parallel stage accurately handle `IEnumerable<object>` / `List<object?>`.
4. **Update Demo Documentation**: Keep project documentation accurate and focused on teaching developers how to utilize native parallel execution in `EtlAnalytics.RulesEngine`.
