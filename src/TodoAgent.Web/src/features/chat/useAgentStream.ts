import { useCallback, useRef, useState } from "react";

// The events the API streams, mirroring AgentEvent.cs one to one.
export type AgentEvent =
  | { kind: "text_delta"; text: string }
  | { kind: "tool_call_requested"; callId: string; name: string; input: unknown }
  | { kind: "tool_call_completed"; callId: string; name: string; result: string; isError: boolean }
  | { kind: "done" }
  | { kind: "error"; message: string }
  | { kind: "max_iterations"; iterations: number };

// What the timeline draws. A tool step starts when the call is requested and is
// filled in when it completes, matched by callId.
export type Step =
  | { id: string; kind: "user"; text: string }
  | { id: string; kind: "assistant"; text: string }
  | { id: string; kind: "tool"; name: string; input: unknown; result?: string; isError?: boolean }
  | { id: string; kind: "notice"; text: string; isError: boolean };

interface Options {
  onToolCompleted?: () => void;
}

export function useAgentStream({ onToolCompleted }: Options = {}) {
  const [steps, setSteps] = useState<Step[]>([]);
  const [busy, setBusy] = useState(false);
  const conversationId = useRef(crypto.randomUUID());
  const counter = useRef(0);

  const nextId = () => `s${++counter.current}`;

  const apply = useCallback(
    (event: AgentEvent) => {
      switch (event.kind) {
        case "text_delta":
          setSteps((s) => [...s, { id: nextId(), kind: "assistant", text: event.text }]);
          break;
        case "tool_call_requested":
          setSteps((s) => [...s, { id: event.callId, kind: "tool", name: event.name, input: event.input }]);
          break;
        case "tool_call_completed":
          setSteps((s) =>
            s.map((step) =>
              step.kind === "tool" && step.id === event.callId
                ? { ...step, result: event.result, isError: event.isError }
                : step,
            ),
          );
          onToolCompleted?.();
          break;
        case "done":
          break;
        case "error":
          setSteps((s) => [...s, { id: nextId(), kind: "notice", text: event.message, isError: true }]);
          break;
        case "max_iterations":
          setSteps((s) => [
            ...s,
            { id: nextId(), kind: "notice", text: `Stopped after ${event.iterations} iterations.`, isError: true },
          ]);
          break;
      }
    },
    [onToolCompleted],
  );

  const send = useCallback(
    async (text: string) => {
      setBusy(true);
      setSteps((s) => [...s, { id: nextId(), kind: "user", text }]);
      try {
        const response = await fetch(`/api/chat/${conversationId.current}/messages`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ text }),
        });
        if (!response.ok || !response.body) {
          throw new Error(`Request failed: ${response.status} ${response.statusText}`);
        }
        for await (const event of readServerSentEvents(response.body)) {
          apply(event);
        }
      } catch (e) {
        const message = e instanceof Error ? e.message : "Something went wrong.";
        setSteps((s) => [...s, { id: nextId(), kind: "notice", text: message, isError: true }]);
      } finally {
        setBusy(false);
      }
    },
    [apply],
  );

  return { steps, busy, send, conversationId: conversationId.current };
}

// EventSource only does GET, so the POST stream is parsed by hand. SSE is lines;
// a blank line ends one event; the JSON is on the data: line.
async function* readServerSentEvents(body: ReadableStream<Uint8Array>): AsyncGenerator<AgentEvent> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let boundary = buffer.indexOf("\n\n");
    while (boundary >= 0) {
      const frame = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      const data = frame
        .split("\n")
        .filter((line) => line.startsWith("data:"))
        .map((line) => line.slice(5).trim())
        .join("\n");
      if (data) yield JSON.parse(data) as AgentEvent;
      boundary = buffer.indexOf("\n\n");
    }
  }
}
