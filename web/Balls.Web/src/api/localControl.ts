import createClient from "openapi-fetch";

import type { components, paths } from "./generated/local-control-v1";

export const localControlClient = createClient<paths>({
  baseUrl: "",
});

export type StatusDto = components["schemas"]["StatusResponse"];
export type CircleDetailsDto = components["schemas"]["CircleDetailsResponse"];
export type CircleListDto = components["schemas"]["CircleListResponse"];
export type CircleSummaryDto = components["schemas"]["CircleResponse"];
export type CircleMessageDto = components["schemas"]["CircleMessageResponse"];
export type CircleMessageListDto =
  components["schemas"]["CircleMessageListResponse"];
export type CreateCircleDto = components["schemas"]["CreateCircleRequest"];
export type JoinBrowserCircleDto =
  components["schemas"]["JoinBrowserCircleRequest"];
export type BrowserInvitationDto =
  components["schemas"]["BrowserCircleInvitationResponse"];
export type CircleViewerDto =
  components["schemas"]["BrowserCircleViewerResponse"];
export type CircleFilesSyncDto =
  components["schemas"]["BrowserCircleFilesSyncResponse"];
export type BrowserSessionDto = components["schemas"]["BrowserSessionResponse"];
export type MemberDto = components["schemas"]["MemberResponse"];
export type CircleNodeDto = components["schemas"]["CircleNodeResponse"];
export type CircleFilesContributionListDto =
  components["schemas"]["CircleFilesContributionListResponse"];
export type MemberAccessGrantListDto =
  components["schemas"]["MemberAccessGrantListResponse"];
export type BrowserCircleFilesOpenDto =
  components["schemas"]["BrowserCircleFilesOpenResponse"];
export type BrowserBallsWizardStatusDto =
  components["schemas"]["BrowserBallsWizardStatusResponse"];
export type BrowserBallsWizardChatDto =
  components["schemas"]["BrowserBallsWizardChatResponse"];
export type BrowserBallsWizardChatMessageDto =
  components["schemas"]["BrowserBallsWizardChatMessageRequest"];
