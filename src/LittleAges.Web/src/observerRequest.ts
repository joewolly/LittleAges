export const OBSERVER_READ_TIMEOUT_MS = 15_000

/** Bound the entire read, including a response body that stalls after headers. */
export async function readObserverResponse<T>(path: string, read: (response: Response) => Promise<T>): Promise<T> {
  const controller = new AbortController()
  let timer: ReturnType<typeof setTimeout> | undefined
  const deadline = new Promise<never>((_, reject) => {
    timer = setTimeout(() => {
      reject(new Error('The server did not respond within 15 seconds. Please try again.'))
      controller.abort()
    }, OBSERVER_READ_TIMEOUT_MS)
  })
  try {
    return await Promise.race([fetch(path, { signal: controller.signal }).then(read), deadline])
  } finally {
    clearTimeout(timer)
  }
}
