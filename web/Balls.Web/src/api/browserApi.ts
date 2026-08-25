import type {
  CircleDetailsDto,
  CircleListDto,
  CircleMessageListDto,
  CreateCircleDto,
  BrowserSessionDto,
  StatusDto,
  CircleFilesContributionListDto,
  MemberAccessGrantListDto,
  CircleFilesMemberMappingPlanDto,
  CircleFilesMemberMappingInspectionDto,
  CircleFilesMemberMappingResultDto,
  BrowserInvitationDto,
  JoinCircleDto,
} from "./localControl";

interface ErrorDto {
  code: string;
  message: string;
}

export interface BrowserApi {
  exchangeLaunchCapability(capability: string): Promise<void>;
  getStatus(): Promise<StatusDto>;
  listCircles(): Promise<CircleListDto>;
  getCircle(circleId: string): Promise<CircleDetailsDto>;
  getMessages(circleId: string): Promise<CircleMessageListDto>;
  createCircle(
    name: string,
    ownerDisplayName: string,
  ): Promise<CircleDetailsDto>;
  createInvitation(
    circleId: string,
    hostAddress?: string,
  ): Promise<BrowserInvitationDto>;
  joinCircle(
    packageValue: string,
    endpoint: string,
    memberDisplayName: string,
  ): Promise<CircleDetailsDto>;
  listFilesContributions(
    circleId: string,
  ): Promise<CircleFilesContributionListDto>;
  listFilesGrants(
    circleId: string,
    contributionId: string,
  ): Promise<MemberAccessGrantListDto>;
  previewFilesMapping(
    circleId: string,
    contributionId: string,
    grantId: string,
    endpoint: string,
    driveLetter: string,
  ): Promise<CircleFilesMemberMappingPlanDto>;
  mapFiles(
    circleId: string,
    contributionId: string,
    grantId: string,
    endpoint: string,
    driveLetter: string,
    planId: string,
  ): Promise<CircleFilesMemberMappingResultDto>;
  inspectFilesMapping(
    circleId: string,
    contributionId: string,
    grantId: string,
    endpoint: string,
    driveLetter: string,
  ): Promise<CircleFilesMemberMappingInspectionDto>;
  unmapFiles(
    circleId: string,
    contributionId: string,
    grantId: string,
    endpoint: string,
    driveLetter: string,
  ): Promise<CircleFilesMemberMappingResultDto>;
}

class FetchBrowserApi implements BrowserApi {
  private antiforgeryToken: string | undefined;

  async exchangeLaunchCapability(capability: string) {
    const session = await this.request<BrowserSessionDto>(
      "/browser/v1/session",
      {
        method: "POST",
        body: JSON.stringify({ capability }),
      },
    );
    this.antiforgeryToken = session.antiforgeryToken;
  }

  getStatus() {
    return this.request<StatusDto>("/browser/v1/status");
  }

  listCircles() {
    return this.request<CircleListDto>("/browser/v1/circles");
  }

  getCircle(circleId: string) {
    return this.request<CircleDetailsDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}`,
    );
  }

  getMessages(circleId: string) {
    return this.request<CircleMessageListDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/messages`,
    );
  }

  createCircle(name: string, ownerDisplayName: string) {
    if (!this.antiforgeryToken) {
      throw new Error("Run balls ui again to create a Circle.");
    }

    const request = {
      requestId: crypto.randomUUID(),
      name,
      ownerDisplayName,
    } satisfies CreateCircleDto;
    return this.request<CircleDetailsDto>("/browser/v1/circles", {
      method: "POST",
      body: JSON.stringify(request),
      headers: {
        "X-Balls-Antiforgery": this.antiforgeryToken,
      },
    });
  }

  createInvitation(circleId: string, hostAddress?: string) {
    return this.authenticatedRequest<BrowserInvitationDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/invitations`,
      { validForMinutes: 60, hostAddress: hostAddress?.trim() || null },
      "Run balls ui again to invite someone.",
    );
  }

  joinCircle(
    packageValue: string,
    endpoint: string,
    memberDisplayName: string,
  ) {
    const request = {
      package: packageValue,
      endpoint,
      memberDisplayName,
    } satisfies JoinCircleDto;
    return this.authenticatedRequest<CircleDetailsDto>(
      "/browser/v1/circles/join",
      request,
      "Run balls ui again to join a Circle.",
    );
  }

  listFilesContributions(circleId: string) {
    return this.request<CircleFilesContributionListDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/files/contributions`,
    );
  }

  listFilesGrants(circleId: string, contributionId: string) {
    return this.request<MemberAccessGrantListDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/files/contributions/${encodeURIComponent(contributionId)}/grants`,
    );
  }

  previewFilesMapping(
    circleId: string,
    contributionId: string,
    grantId: string,
    endpoint: string,
    driveLetter: string,
  ) {
    return this.mappingRequest<CircleFilesMemberMappingPlanDto>(
      circleId,
      contributionId,
      grantId,
      "preview",
      { endpoint, driveLetter },
    );
  }

  mapFiles(
    circleId: string,
    contributionId: string,
    grantId: string,
    endpoint: string,
    driveLetter: string,
    planId: string,
  ) {
    return this.mappingRequest<CircleFilesMemberMappingResultDto>(
      circleId,
      contributionId,
      grantId,
      "map",
      { endpoint, driveLetter, planId },
    );
  }

  inspectFilesMapping(
    circleId: string,
    contributionId: string,
    grantId: string,
    endpoint: string,
    driveLetter: string,
  ) {
    return this.mappingRequest<CircleFilesMemberMappingInspectionDto>(
      circleId,
      contributionId,
      grantId,
      "inspect",
      { endpoint, driveLetter },
    );
  }

  unmapFiles(
    circleId: string,
    contributionId: string,
    grantId: string,
    endpoint: string,
    driveLetter: string,
  ) {
    return this.mappingRequest<CircleFilesMemberMappingResultDto>(
      circleId,
      contributionId,
      grantId,
      "unmap",
      { endpoint, driveLetter },
    );
  }

  private mappingRequest<T>(
    circleId: string,
    contributionId: string,
    grantId: string,
    operation: "preview" | "map" | "inspect" | "unmap",
    body: Record<string, string>,
  ) {
    return this.authenticatedRequest<T>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/files/contributions/${encodeURIComponent(contributionId)}/grants/${encodeURIComponent(grantId)}/mapping/${operation}`,
      body,
      "Run balls ui again to change an Explorer mapping.",
    );
  }

  private authenticatedRequest<T>(
    path: string,
    body: object,
    missingSessionMessage: string,
  ) {
    if (!this.antiforgeryToken) {
      throw new Error(missingSessionMessage);
    }
    return this.request<T>(path, {
      method: "POST",
      body: JSON.stringify(body),
      headers: { "X-Balls-Antiforgery": this.antiforgeryToken },
    });
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(path, {
      ...init,
      credentials: "same-origin",
      headers: {
        Accept: "application/json",
        ...(init.body ? { "Content-Type": "application/json" } : {}),
        ...init.headers,
      },
    });
    if (!response.ok) {
      let message = `ballsd rejected the browser request (${response.status}).`;
      try {
        const error = (await response.json()) as ErrorDto;
        if (typeof error.message === "string" && error.message.length > 0) {
          message = error.message;
        }
      } catch {
        // Keep the bounded generic message for malformed responses.
      }
      throw new Error(message);
    }

    return (await response.json()) as T;
  }
}

export const browserApi: BrowserApi = new FetchBrowserApi();
