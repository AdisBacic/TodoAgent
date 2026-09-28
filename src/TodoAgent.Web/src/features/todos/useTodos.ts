import { useCallback, useEffect, useState } from "react";
import {
  ApiError,
  completeTodo,
  createTodo,
  deleteTodo,
  listTodos,
  type Todo,
} from "./api";

// Holds the list and exposes the actions. Every action re-reads the list from
// the server afterwards rather than patching local state: the API is the source
// of truth, and (from chapter 2) the agent changes todos behind the UI's back.
export function useTodos() {
  const [todos, setTodos] = useState<Todo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    try {
      setTodos(await listTodos());
      setError(null);
    } catch (e) {
      setError(describe(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const run = useCallback(
    async (action: () => Promise<unknown>) => {
      try {
        await action();
        await reload();
      } catch (e) {
        setError(describe(e));
      }
    },
    [reload],
  );

  const add = useCallback((title: string) => run(() => createTodo(title)), [run]);
  const complete = useCallback((id: number) => run(() => completeTodo(id)), [run]);
  const remove = useCallback((id: number) => run(() => deleteTodo(id)), [run]);

  return { todos, error, loading, reload, add, complete, remove };
}

function describe(e: unknown): string {
  if (e instanceof ApiError) return e.message;
  if (e instanceof Error) return e.message;
  return "Something went wrong.";
}
