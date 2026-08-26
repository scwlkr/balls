export function toMessage(reason: unknown) {
  return reason instanceof Error
    ? reason.message
    : "The local workspace could not be loaded.";
}
