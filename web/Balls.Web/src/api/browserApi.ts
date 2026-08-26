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
  CircleViewerDto,
  CircleFilesSyncDto,
  JoinBrowserCircleDto,
} from "./localControl";

interface ErrorDto {
  code: string;
  message: string;
}

export interface CircleFilesFolderSelectionDto {
  status: "selected" | "cancelled";
  folderPath: string | null;
  displayName: string | null;
}

export interface CircleFilesContributionResultDto {
  status: "applied" | "already-applied";
  contributionId: string;
  displayName: string;
  folderPath: string;
}

export interface CircleFilesGrantPreviewDto {
  folderName: string;
  folderPath: string;
  memberName: string;
  access: "Read/write";
  summary: string;
}

export interface CircleFilesGrantResultDto {
  status: "applied" | "already-applied";
  folderName: string;
  memberName: string;
  access: "Read/write";
  message: string;
}

export interface BrowserApi {
  exchangeLaunchCapability(capability: string): Promise<void>;
  getStatus(): Promise<StatusDto>;
  listCircles(): Promise<CircleListDto>;
  getCircle(circleId: string): Promise<CircleDetailsDto>;
  getViewer(circleId: string): Promise<CircleViewerDto>;
  getMessages(circleId: string): Promise<CircleMessageListDto>;
  createCircle(
    name: string,
    ownerDisplayName: string,
  ): Promise<CircleDetailsDto>;
  createInvitation(circleId: string): Promise<BrowserInvitationDto>;
  joinCircle(
    packageValue: string,
    provider: string,
    admissionEndpoint: string,
    syncEndpoint: string,
    memberDisplayName: string,
  ): Promise<CircleDetailsDto>;
  syncFiles(circleId: string): Promise<CircleFilesSyncDto>;
  listFilesContributions(
    circleId: string,
  ): Promise<CircleFilesContributionListDto>;
  selectFilesFolder(circleId: string): Promise<CircleFilesFolderSelectionDto>;
  contributeFilesFolder(
    circleId: string,
    requestId: string,
    folderPath: string,
    displayName: string,
  ): Promise<CircleFilesContributionResultDto>;
  previewFilesGrant(
    circleId: string,
    folderName: string,
    memberName: string,
  ): Promise<CircleFilesGrantPreviewDto>;
  applyFilesGrant(circleId: string): Promise<CircleFilesGrantResultDto>;
  listFilesGrants(
    circleId: string,
    contributionId: string,
  ): Promise<MemberAccessGrantListDto>;
  previewFilesMapping(
    circleId: string,
    contributionId: string,
    grantId: string,
    driveLetter: string,
  ): Promise<CircleFilesMemberMappingPlanDto>;
  mapFiles(
    circleId: string,
    contributionId: string,
    grantId: string,
    driveLetter: string,
    planId: string,
  ): Promise<CircleFilesMemberMappingResultDto>;
  inspectFilesMapping(
    circleId: string,
    contributionId: string,
    grantId: string,
    driveLetter: string,
  ): Promise<CircleFilesMemberMappingInspectionDto>;
  unmapFiles(
    circleId: string,
    contributionId: string,
    grantId: string,
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

  getViewer(circleId: string) {
    return this.request<CircleViewerDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/viewer`,
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

  createInvitation(circleId: string) {
    return this.authenticatedRequest<BrowserInvitationDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/invitations`,
      { validForMinutes: 60 },
      "Run balls ui again to invite someone.",
    );
  }

  joinCircle(
    packageValue: string,
    provider: string,
    admissionEndpoint: string,
    syncEndpoint: string,
    memberDisplayName: string,
  ) {
    const request = {
      package: packageValue,
      provider,
      admissionEndpoint,
      syncEndpoint,
      memberDisplayName,
    } satisfies JoinBrowserCircleDto;
    return this.authenticatedRequest<CircleDetailsDto>(
      "/browser/v1/circles/join",
      request,
      "Run balls ui again to join a Circle.",
    );
  }

  syncFiles(circleId: string) {
    return this.authenticatedRequest<CircleFilesSyncDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/files/sync`,
      {},
      "Run balls ui again to connect your shared files.",
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

  selectFilesFolder(circleId: string) {
    return this.authenticatedRequest<CircleFilesFolderSelectionDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/files/contributions/folder-selection`,
      {},
      "Run balls ui again to choose a folder.",
    );
  }

  contributeFilesFolder(
    circleId: string,
    requestId: string,
    folderPath: string,
    displayName: string,
  ) {
    return this.authenticatedRequest<CircleFilesContributionResultDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/files/contributions/folder-apply`,
      { requestId, folderPath, displayName },
      "Run balls ui again to contribute the folder.",
    );
  }

  previewFilesGrant(circleId: string, folderName: string, memberName: string) {
    return this.authenticatedRequest<CircleFilesGrantPreviewDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/files/grant/preview`,
      { folderName, memberName, access: "read-write" },
      "Run balls ui again to review Member access.",
    );
  }

  applyFilesGrant(circleId: string) {
    return this.authenticatedRequest<CircleFilesGrantResultDto>(
      `/browser/v1/circles/${encodeURIComponent(circleId)}/files/grant/apply`,
      {},
      "Run balls ui again to share the folder.",
    );
  }

  previewFilesMapping(
    circleId: string,
    contributionId: string,
    grantId: string,
    driveLetter: string,
  ) {
    return this.mappingRequest<CircleFilesMemberMappingPlanDto>(
      circleId,
      contributionId,
      grantId,
      "preview",
      { driveLetter },
    );
  }

  mapFiles(
    circleId: string,
    contributionId: string,
    grantId: string,
    driveLetter: string,
    planId: string,
  ) {
    return this.mappingRequest<CircleFilesMemberMappingResultDto>(
      circleId,
      contributionId,
      grantId,
      "map",
      { driveLetter, planId },
    );
  }

  inspectFilesMapping(
    circleId: string,
    contributionId: string,
    grantId: string,
    driveLetter: string,
  ) {
    return this.mappingRequest<CircleFilesMemberMappingInspectionDto>(
      circleId,
      contributionId,
      grantId,
      "inspect",
      { driveLetter },
    );
  }

  unmapFiles(
    circleId: string,
    contributionId: string,
    grantId: string,
    driveLetter: string,
  ) {
    return this.mappingRequest<CircleFilesMemberMappingResultDto>(
      circleId,
      contributionId,
      grantId,
      "unmap",
      { driveLetter },
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
