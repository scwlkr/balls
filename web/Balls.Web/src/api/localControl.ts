import createClient from "openapi-fetch";

import type { components, paths } from "./generated/local-control-v1";

export const localControlClient = createClient<paths>({
  baseUrl: "",
});

export type StatusDto = components["schemas"]["StatusResponse"];
export type CircleDetailsDto = components["schemas"]["CircleDetailsResponse"];
export type MemberDto = components["schemas"]["MemberResponse"];
export type CircleNodeDto = components["schemas"]["CircleNodeResponse"];
