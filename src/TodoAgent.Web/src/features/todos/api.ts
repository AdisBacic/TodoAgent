// The four todo endpoints, one function each. This file is the only place the
// todo feature knows about URLs or HTTP.

export interface Todo {
  id: number;
  title: string;
  isCompleted: boolean;
  createdAt: string;
  completedAt: string | null;
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly errors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

async function throwProblem(response: Response): Promise<never> {
  let problem: ProblemDetails = {};
  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // Not a problem+json body; fall back to the status text.
  }
  const firstError = Object.values(problem.errors ?? {}).flat()[0];
  throw new ApiError(
    response.status,
    firstError ?? problem.detail ?? problem.title ?? response.statusText,
    problem.errors,
  );
}

export async function listTodos(): Promise<Todo[]> {
  const response = await fetch("/api/todos");
  if (!response.ok) await throwProblem(response);
  const body = (await response.json()) as { todos: Todo[] };
  return body.todos;
}

export async function createTodo(title: string): Promise<Todo> {
  const response = await fetch("/api/todos", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
  if (!response.ok) await throwProblem(response);
  return (await response.json()) as Todo;
}
