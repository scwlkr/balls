export interface paths {
  "/control/v1/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody?: never;
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["StatusResponse"];
          };
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/control/v1/circles": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody?: never;
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["CircleListResponse"];
          };
        };
      };
    };
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody: {
        content: {
          "application/json": components["schemas"]["CreateCircleRequest"];
        };
      };
      responses: {
        /** @description Created */
        201: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["CircleDetailsResponse"];
          };
        };
        /** @description Bad Request */
        400: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["ErrorResponse"];
          };
        };
        /** @description Conflict */
        409: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["ErrorResponse"];
          };
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/control/v1/circles/{circleId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          circleId: string;
        };
        cookie?: never;
      };
      requestBody?: never;
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["CircleDetailsResponse"];
          };
        };
        /** @description Bad Request */
        400: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["ErrorResponse"];
          };
        };
        /** @description Not Found */
        404: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["ErrorResponse"];
          };
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/control/v1/circles/{circleId}/members": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          circleId: string;
        };
        cookie?: never;
      };
      requestBody?: never;
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["MemberListResponse"];
          };
        };
        /** @description Bad Request */
        400: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["ErrorResponse"];
          };
        };
        /** @description Not Found */
        404: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["ErrorResponse"];
          };
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/control/v1/circles/{circleId}/nodes": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          circleId: string;
        };
        cookie?: never;
      };
      requestBody?: never;
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["NodeListResponse"];
          };
        };
        /** @description Bad Request */
        400: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["ErrorResponse"];
          };
        };
        /** @description Not Found */
        404: {
          headers: {
            [name: string]: unknown;
          };
          content: {
            "application/json": components["schemas"]["ErrorResponse"];
          };
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
}
export type webhooks = Record<string, never>;
export interface components {
  schemas: {
    CircleDetailsResponse: {
      circle: components["schemas"]["CircleResponse"];
      members: components["schemas"]["MemberResponse"][];
      nodes: components["schemas"]["CircleNodeResponse"][];
    };
    CircleListResponse: {
      circles: components["schemas"]["CircleResponse"][];
    };
    CircleNodeResponse: {
      id: string;
      displayName: string;
      /** Format: date-time */
      joinedAtUtc: string;
    };
    CircleResponse: {
      id: string;
      name: string;
      /** Format: date-time */
      createdAtUtc: string;
      /** Format: int32 */
      memberCount: number | string;
      /** Format: int32 */
      nodeCount: number | string;
    };
    CreateCircleRequest: {
      requestId: string;
      name: string;
      ownerDisplayName: string;
    };
    ErrorResponse: {
      code: string;
      message: string;
    };
    MemberListResponse: {
      circleId: string;
      members: components["schemas"]["MemberResponse"][];
    };
    MemberResponse: {
      id: string;
      displayName: string;
      role: string;
      /** Format: date-time */
      joinedAtUtc: string;
    };
    NodeListResponse: {
      circleId: string;
      nodes: components["schemas"]["CircleNodeResponse"][];
    };
    NodeResponse: {
      id: string;
      displayName: string;
      /** Format: date-time */
      createdAtUtc: string;
    };
    StatusResponse: {
      productVersion: string;
      /** Format: int32 */
      protocolVersion: number | string;
      node: components["schemas"]["NodeResponse"];
    };
  };
  responses: never;
  parameters: never;
  requestBodies: never;
  headers: never;
  pathItems: never;
}
export type $defs = Record<string, never>;
export type operations = Record<string, never>;
