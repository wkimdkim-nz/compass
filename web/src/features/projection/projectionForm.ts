// What the calculator's form holds, and its two crossings of the units line: wire values in as
// field text, and field text out as the drafts the calculator takes and the body a save sends.

import { calculateProjection, type DraftAssumptionSet, type DraftBaseFacts, type ProjectionResult } from "../../projection/calculator";
import { parseFigure, toUi, toWire, type Unit } from "./figures";
import type {
  BaseFactsResponse,
  ExitMultipleKind,
  SavedScenario,
  SaveProjectionRequest,
  ScenarioName,
  SeededScenario,
} from "./projectionApi";

export const SCENARIO_NAMES = ["bear", "base", "bull"] as const satisfies readonly ScenarioName[];

/**
 * The text of every field. Any of it may be empty or mistyped at any moment. The current net margin
 * is not among them: no formula takes it, so it is printed, not typed (see `currentMarginText`).
 */
export type BaseFactsForm = { price: string; revenue: string; shares: string };
export type ScenarioForm = {
  horizon: string;
  growth: string;
  margin: string;
  multiple: string;
  kind: ExitMultipleKind;
  dilution: string;
};
export type ProjectionForm = { facts: BaseFactsForm; scenarios: Record<ScenarioName, ScenarioForm> };

export type ScenarioField = Exclude<keyof ScenarioForm, "kind">;

export const FACT_UNITS: Record<keyof BaseFactsForm, Unit> = {
  price: "plain",
  revenue: "millions",
  shares: "millions",
};

/** The current net margin as the statement prints it; empty when the lookup gave none. */
export const currentMarginText = (baseFacts: BaseFactsResponse | undefined) => toUi(baseFacts?.currentNetMargin, "percent");

export const SCENARIO_UNITS: Record<ScenarioField, Unit> = {
  horizon: "plain",
  growth: "percent",
  margin: "percent",
  multiple: "multiple",
  dilution: "percent",
};

const emptyScenario = (): ScenarioForm => ({ horizon: "", growth: "", margin: "", multiple: "", kind: "pe", dilution: "" });

const scenarioForm = (scenario: SeededScenario | SavedScenario | undefined): ScenarioForm =>
  scenario
    ? {
        horizon: toUi(scenario.horizon, SCENARIO_UNITS.horizon),
        growth: toUi(scenario.growthRate, SCENARIO_UNITS.growth),
        margin: toUi(scenario.futureNetMargin, SCENARIO_UNITS.margin),
        multiple: toUi(scenario.exitMultiple.value, SCENARIO_UNITS.multiple),
        kind: scenario.exitMultiple.kind,
        dilution: toUi(scenario.dilutionRate, SCENARIO_UNITS.dilution),
      }
    : emptyScenario();

/** The form a page opens with: whatever base facts there are, under the given scenarios, by name. */
export function buildForm(
  baseFacts: BaseFactsResponse | undefined,
  scenarios: readonly (SeededScenario | SavedScenario)[],
): ProjectionForm {
  const named = (name: ScenarioName) => scenarioForm(scenarios.find((s) => s.name === name));
  return {
    facts: {
      price: toUi(baseFacts?.currentPrice, FACT_UNITS.price),
      revenue: toUi(baseFacts?.ttmRevenue, FACT_UNITS.revenue),
      shares: toUi(baseFacts?.dilutedShares, FACT_UNITS.shares),
    },
    scenarios: { bear: named("bear"), base: named("base"), bull: named("bull") },
  };
}

/** A field as the wire carries it: null when empty, NaN when mistyped. */
const wire = (text: string, unit: Unit): number | null => {
  const value = parseFigure(text);
  return value === null ? null : toWire(value, unit);
};

const factsDraft = (facts: BaseFactsForm): DraftBaseFacts => ({
  currentPrice: wire(facts.price, FACT_UNITS.price),
  ttmRevenue: wire(facts.revenue, FACT_UNITS.revenue),
  dilutedShares: wire(facts.shares, FACT_UNITS.shares),
});

const scenarioDraft = (scenario: ScenarioForm): DraftAssumptionSet => ({
  horizon: wire(scenario.horizon, SCENARIO_UNITS.horizon),
  growthRate: wire(scenario.growth, SCENARIO_UNITS.growth),
  futureNetMargin: wire(scenario.margin, SCENARIO_UNITS.margin),
  exitMultiple: { kind: scenario.kind, value: wire(scenario.multiple, SCENARIO_UNITS.multiple) },
  dilutionRate: wire(scenario.dilution, SCENARIO_UNITS.dilution),
});

/** The live preview: each column through the TypeScript calculator, as the form stands. */
export function preview(form: ProjectionForm): Record<ScenarioName, ProjectionResult> {
  const facts = factsDraft(form.facts);
  const column = (name: ScenarioName) => calculateProjection(facts, scenarioDraft(form.scenarios[name]));
  return { bear: column("bear"), base: column("base"), bull: column("bull") };
}

/** A column nobody has typed in. It is left out of a save; the P/E or P/S choice alone is not typing. */
const untouched = ({ kind: _kind, ...fields }: ScenarioForm) => Object.values(fields).every((text) => text.trim() === "");

export type SaveDraft =
  | { ready: true; request: SaveProjectionRequest }
  /** The columns that are started but do not compute; none when no column has been started at all. */
  | { ready: false; unfinished: ScenarioName[] };

/**
 * The body of a save, once every column that has been started computes. The server is the
 * authority on what is valid; this only keeps a half-typed form from being sent to it.
 */
export function saveDraft(
  form: ProjectionForm,
  company: { ticker: string; companyName: string; cik: number | null },
): SaveDraft {
  const results = preview(form);
  const started = SCENARIO_NAMES.filter((name) => !untouched(form.scenarios[name]));
  // The preview needs no margin on the P/S path, but a save does: the API stores one for every
  // scenario and reads a figure left out as missing, never as zero.
  const unfinished = started.filter(
    (name) => results[name].status !== "ok" || !Number.isFinite(scenarioDraft(form.scenarios[name]).futureNetMargin),
  );
  if (started.length === 0 || unfinished.length > 0) return { ready: false, unfinished };

  const facts = factsDraft(form.facts);
  return {
    ready: true,
    request: {
      ...company,
      baseFacts: {
        currentPrice: facts.currentPrice ?? null,
        ttmRevenue: facts.ttmRevenue ?? null,
        dilutedShares: facts.dilutedShares ?? null,
      },
      scenarios: started.map((name) => {
        const draft = scenarioDraft(form.scenarios[name]);
        return {
          name,
          growthRate: draft.growthRate!,
          futureNetMargin: draft.futureNetMargin!,
          exitMultiple: { kind: form.scenarios[name].kind, value: draft.exitMultiple!.value! },
          dilutionRate: draft.dilutionRate!,
          horizon: draft.horizon!,
        };
      }),
    },
  };
}
