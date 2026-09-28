import type { Todo } from "./api";

interface Props {
  todo: Todo;
  onComplete: (id: number) => void;
  onDelete: (id: number) => void;
}

export function TodoItem({ todo, onComplete, onDelete }: Props) {
  return (
    <li className={"todo-item" + (todo.isCompleted ? " is-completed" : "")}>
      <span className="todo-id">#{todo.id}</span>
      <span className="todo-title">{todo.title}</span>
      <span className="todo-actions">
        {!todo.isCompleted && (
          <button type="button" onClick={() => onComplete(todo.id)}>
            Done
          </button>
        )}
        <button type="button" onClick={() => onDelete(todo.id)} aria-label={`Delete #${todo.id}`}>
          Delete
        </button>
      </span>
    </li>
  );
}
