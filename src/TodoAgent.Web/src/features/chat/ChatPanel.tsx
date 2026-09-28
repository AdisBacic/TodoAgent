import { useState, type FormEvent } from "react";
import { StepTimeline } from "./StepTimeline";
import { useAgentStream } from "./useAgentStream";

interface Props {
  onTodosChanged: () => void;
}

export function ChatPanel({ onTodosChanged }: Props) {
  const { steps, busy, send } = useAgentStream({ onToolCompleted: onTodosChanged });
  const [text, setText] = useState("");

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const trimmed = text.trim();
    if (!trimmed || busy) return;
    setText("");
    await send(trimmed);
  }

  return (
    <section className="panel chat">
      <h2>Chat</h2>
      <StepTimeline steps={steps} busy={busy} />
      <form className="chat-form" onSubmit={onSubmit}>
        <input
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="Tell the agent what to do…"
          aria-label="Chat message"
          disabled={busy}
        />
        <button type="submit" disabled={busy || !text.trim()}>
          Send
        </button>
      </form>
    </section>
  );
}
