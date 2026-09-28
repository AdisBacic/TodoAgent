import { ChatPanel } from "./features/chat/ChatPanel";
import { TodoList } from "./features/todos/TodoList";
import { useTodos } from "./features/todos/useTodos";

export default function App() {
  const todos = useTodos();

  return (
    <div className="app">
      <header className="app-header">
        <h1>TodoAgent</h1>
        <p className="muted">Two front doors to the same four features: the list, and an agent with tools.</p>
      </header>
      <main className="app-main two-column">
        <TodoList {...todos} />
        <ChatPanel onTodosChanged={todos.reload} />
      </main>
    </div>
  );
}
