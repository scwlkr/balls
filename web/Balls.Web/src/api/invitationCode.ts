import type { BrowserInvitationDto } from "./localControl";

const invitationPrefix = "BALLS1.";
const maximumInvitationLength = 32 * 1024;

interface InvitationEnvelope {
  version: 1;
  endpoint: string;
  syncEndpoint: string;
  package: string;
}

export function encodeInvitationCode(
  invitation: Pick<
    BrowserInvitationDto,
    "endpoint" | "syncEndpoint" | "package"
  >,
) {
  const envelope = JSON.stringify({
    version: 1,
    endpoint: invitation.endpoint,
    syncEndpoint: invitation.syncEndpoint,
    package: invitation.package,
  } satisfies InvitationEnvelope);
  const bytes = new TextEncoder().encode(envelope);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return (
    invitationPrefix +
    btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "")
  );
}

export function decodeInvitationCode(value: string): InvitationEnvelope {
  const code = value.trim();
  if (
    !code.startsWith(invitationPrefix) ||
    code.length > maximumInvitationLength ||
    !/^[-A-Za-z0-9_]+$/.test(code.slice(invitationPrefix.length))
  ) {
    throw new Error(
      "Paste the complete invitation shared by your Circle owner.",
    );
  }

  try {
    const encoded = code
      .slice(invitationPrefix.length)
      .replaceAll("-", "+")
      .replaceAll("_", "/");
    const binary = atob(encoded);
    const bytes = Uint8Array.from(binary, (character) =>
      character.charCodeAt(0),
    );
    const invitation: unknown = JSON.parse(new TextDecoder().decode(bytes));
    if (
      typeof invitation !== "object" ||
      invitation === null ||
      !("version" in invitation) ||
      invitation.version !== 1 ||
      !("endpoint" in invitation) ||
      typeof invitation.endpoint !== "string" ||
      !("syncEndpoint" in invitation) ||
      typeof invitation.syncEndpoint !== "string" ||
      !("package" in invitation) ||
      typeof invitation.package !== "string" ||
      invitation.package.length === 0
    ) {
      throw new Error("invalid invitation");
    }
    return {
      version: 1,
      endpoint: invitation.endpoint,
      syncEndpoint: invitation.syncEndpoint,
      package: invitation.package,
    };
  } catch {
    throw new Error(
      "This invitation is incomplete or invalid. Ask for a new one.",
    );
  }
}

export function invitationHostAddress(endpoint: string) {
  const separator = endpoint.lastIndexOf(":");
  return separator > 0 ? endpoint.slice(0, separator) : "";
}
