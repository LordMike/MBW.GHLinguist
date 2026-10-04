export interface User { id: number; name?: string }
export function byId<T extends User>(items: readonly T[], id: number): T | undefined {
  return items.find((u) => u.id === id);
}
