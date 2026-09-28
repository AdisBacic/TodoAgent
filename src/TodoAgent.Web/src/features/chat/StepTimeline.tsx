import type { Step } from "./useAgentStream";

interface Props {
  steps: Step[];
  busy: boolean;
}

// One row per step: what you said, what the model said, and every tool call
// with its result. This is the agent loop made visible.
export function StepTimeline({ steps, busy }: Props) {
  if (steps.length === 0) {
    return <p className="muted">Try: “add buy milk and mark the dentist one done”.</p>;
  }

  return (
    <ol className="timeline">
      {steps.map((step) => (
        <li key={step.id} className={`step step-${step.kind}`}>
          {step.kind === "user" && <span className="step-text">{step.text}</span>}
          {step.kind === "assistant" && <span className="step-text">{step.text}</span>}
          {step.kind === "tool" && (
            <div className="tool-call">
              <code className="tool-request">
                → {step.name}({formatInput(step.input)})
              </code>
              {step.result === undefined ? (
                <span className="muted">running…</span>
              ) : (
                <code className={"tool-result" + (step.isError ? " is-error" : "")}>
                  ← {step.isError ? "error: " : ""}
                  {truncate(step.result)}
                </code>
              )}
            </div>
          )}
          {step.kind === "notice" && (
            <span className={"step-text" + (step.isError ? " is-error" : "")}>{step.text}</span>
          )}
        </li>
      ))}
      {busy && (
        <li className="step step-busy">
          <span className="muted">thinking…</span>
        </li>
      )}
    </ol>
  );
}

function formatInput(input: unknown): string {
  if (input === null || input === undefined) return "";
  if (typeof input === "object" && Object.keys(input as object).length === 0) return "";
  return JSON.stringify(input);
}

function truncate(text: string, max = 240): string {
  return text.length > max ? text.slice(0, max) + "…" : text;
}
