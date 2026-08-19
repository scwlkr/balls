import type {
  CircleDetailsDto,
  CircleListDto,
  CreateCircleDto,
  StatusDto,
} from "./localControl";

interface BrowserSessionDto {
  antiforgeryToken: string;
  expiresAtUtc: string;
}

interface ErrorDto {
  code: string;
  message: string;
}

export interface BrowserApi {
  exchangeLaunchCapability(capability: string): Promise<void>;
  getStatus(): Promise<StatusDto>;
  listCircles(): Promise<CircleListDto>;
  getCircle(circleId: string): Promise<CircleDetailsDto>;
  createCircle(
    name: string,
    ownerDisplayName: string,
  ): Promise<CircleDetailsDto>;
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
