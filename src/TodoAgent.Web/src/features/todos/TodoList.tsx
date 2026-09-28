import { useState, type FormEvent } from "react";
import type { Todo } from "./api";
import { TodoItem } from "./TodoItem";

interface Props {
  todos: Todo[];
  error: string | null;
  loading: boolean;
  add: (title: string) => Promise<void>;
  complete: (id: number) => Promise<void>;
  remove: (id: number) => Promise<void>;
}

export function TodoList({ todos, error, loading, add, complete, remove }: Props) {
  const [title, setTitle] = useState("");

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const trimmed = title.trim();
    if (!trimmed) return;
    await add(trimmed);
    setTitle("");
  }

  return (
    <section className="panel">
      <h2>Todos</h2>
      <form className="todo-form" onSubmit={onSubmit}>
        <input
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="What needs doing?"
          aria-label="New todo title"
        />
        <button type="submit" disabled={!title.trim()}>
          Add
        </button>
      </form>
      {error && <p className="error">{error}</p>}
      {loading ? (
        <p className="muted">Loading…</p>
      ) : todos.length === 0 ? (
        <p className="muted">Nothing yet. Add one above.</p>
      ) : (
        <ul className="todo-list">
          {todos.map((todo) => (
            <TodoItem key={todo.id} todo={todo} onComplete={complete} onDelete={remove} />
          ))}
        </ul>
      )}
    </section>
  );
}
