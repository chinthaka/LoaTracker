export type LoaEvent = {
  status: string;
  adviserName: string;
  mememberEmail: string;
  schemeName: string;
  timestamp?: string;
  documentFileName?: string | null;
};

export type LoaStatus = {
  trackingId: string;
  currentStatus: string;
  history: LoaEvent[];
};

export type LoaUpload = {
  adviserName: string;
  memberEmail: string;
  schemeName: string;
  file: File;
};

export class ApiError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ApiError";
  }
}

export async function uploadLoa(upload: LoaUpload): Promise<{ trackingId: string }> {
  const body = new FormData();
  body.append("AdviserName", upload.adviserName);
  body.append("MemeberEmail", upload.memberEmail);
  body.append("SchemeName", upload.schemeName);
  body.append("file", upload.file);

  const response = await fetch("/loa/upload", { method: "POST", body });
  if (!response.ok) {
    throw new ApiError(await readError(response));
  }

  const payload = (await response.json()) as { trackingId?: string };
  if (!payload.trackingId) {
    throw new ApiError("The service did not return a tracking id.");
  }

  return { trackingId: payload.trackingId };
}

export async function getLoaStatus(trackingId: string): Promise<LoaStatus> {
  const response = await fetch(`/loa/${encodeURIComponent(trackingId)}`);
  if (response.status === 404) {
    throw new ApiError("No letter of authority was found for that tracking id.");
  }
  if (!response.ok) {
    throw new ApiError(await readError(response));
  }

  return (await response.json()) as LoaStatus;
}

async function readError(response: Response): Promise<string> {
  const text = await response.text();
  if (!text) {
    return "The request could not be completed.";
  }

  try {
    const problem = JSON.parse(text) as { errors?: Record<string, string[]>; title?: string };
    const messages = Object.values(problem.errors ?? {}).flat();
    if (messages.length > 0) {
      return messages.join(" ");
    }
    return problem.title || text;
  } catch {
    return text;
  }
}
