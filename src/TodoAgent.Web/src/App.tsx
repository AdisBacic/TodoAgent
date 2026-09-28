import { TodoList } from "./features/todos/TodoList";
import { useTodos } from "./features/todos/useTodos";

export default function App() {
  const todos = useTodos();

  return (
    <div className="app">
      <header className="app-header">
        <h1>TodoAgent</h1>
        <p className="muted">Chapter 1: four slices behind one list. No AI yet.</p>
      </header>
      <main className="app-main">
        <TodoList {...todos} />
      </main>
    </div>
  );
}
