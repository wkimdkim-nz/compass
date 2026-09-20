import { useRef, useState } from "react";
import type { ProjectionOutputs } from "../../projection/calculator";
import { detail, fieldErrors, isStatus } from "../../app/problemDetails";
import {
  useCreateProjectionMutation,
  useReplaceProjectionMutation,
  type Company,
  type SavedProjectionResponse,
  type ScenarioName,
} from "./projectionApi";
import { saveDraft, SCENARIO_TITLE, type ProjectionForm } from "./projectionForm";

type ServerOutputs = Partial<Record<ScenarioName, ProjectionOutputs>>;

const outputsByName = (response: SavedProjectionResponse): ServerOutputs =>
  Object.fromEntries(response.scenarios.map((s) => [s.name, s.outputs]));

type Options = {
  company: Company;
  /** Whether the user came with a projection saved for this company: the first save then replaces. */
  alreadySaved: boolean;
  /** Called with the form a save carried, once the server has taken it. */
  onSaved: (form: ProjectionForm) => void;
};

/**
 * The save path of the calculator: a create or, once a projection exists, a replace; what the
 * server refused, as `problems`; and the .NET calculator's outputs, held until the next edit.
 */
export function useSaveProjection({ company, alreadySaved, onSaved }: Options) {
  const [savedHere, setSavedHere] = useState(false);
  const [serverOutputs, setServerOutputs] = useState<ServerOutputs | null>(null);
  const [problems, setProblems] = useState<string[]>([]);

  const [create, creating] = useCreateProjectionMutation();
  const [replace, replacing] = useReplaceProjectionMutation();
  const exists = alreadySaved || savedHere;

  // Counts edits, so that a save answered after a later edit does not put its outputs over the newer form.
  const edits = useRef(0);

  /** The form changed: what the server last said, outputs and problems alike, is about older figures. */
  const noteEdit = () => {
    edits.current += 1;
    setServerOutputs(null);
    setProblems([]);
  };

  const save = async (form: ProjectionForm) => {
    const draft = saveDraft(form, company);
    if (!draft.ready) {
      setProblems([
        draft.unfinished.length > 0
          ? `Finish or clear the ${draft.unfinished.map((name) => SCENARIO_TITLE[name]).join(" and ")} column before saving: every figure in it, the future net margin included, and the base facts must give a result.`
          : "Fill in at least one scenario before saving.",
      ]);
      return;
    }

    const sentAt = edits.current;
    const result = await (exists ? replace : create)(draft.request);
    if ("error" in result) {
      // A 401 has already sent the app back to sign in (see the store); there is nothing to say here.
      if (isStatus(result.error, 401)) return;
      const fields = [...new Set(Object.values(fieldErrors(result.error)).flat())];
      setProblems(fields.length > 0 ? fields : [detail(result.error) ?? "The projection could not be saved just now. Try again shortly."]);
      return;
    }
    // The form stays editable during a save: outputs for figures since typed over are not shown.
    if (edits.current === sentAt) setServerOutputs(outputsByName(result.data));
    setSavedHere(true);
    setProblems([]);
    onSaved(form);
  };

  return { save, noteEdit, exists, busy: creating.isLoading || replacing.isLoading, serverOutputs, problems };
}
