import createClient from "openapi-fetch";

import type { components, paths } from "./generated/local-control-v1";

export const localControlClient = createClient<paths>({
  baseUrl: "",
});

export type StatusDto = components["schemas"]["StatusResponse"];
export type CircleDetailsDto = components["schemas"]["CircleDetailsResponse"];
export type CircleListDto = components["schemas"]["CircleListResponse"];
export type CircleSummaryDto = components["schemas"]["CircleResponse"];
export type CreateCircleDto = components["schemas"]["CreateCircleRequest"];
export type MemberDto = components["schemas"]["MemberResponse"];
export type CircleNodeDto = components["schemas"]["CircleNodeResponse"];
