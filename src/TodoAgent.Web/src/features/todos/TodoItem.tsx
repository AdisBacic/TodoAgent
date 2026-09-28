import type { Todo } from "./api";

interface Props {
  todo: Todo;
}

export function TodoItem({ todo }: Props) {
  return (
    <li className={"todo-item" + (todo.isCompleted ? " is-completed" : "")}>
      <span className="todo-id">#{todo.id}</span>
      <span className="todo-title">{todo.title}</span>
    </li>
  );
}
