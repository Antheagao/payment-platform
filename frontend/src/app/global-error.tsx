"use client";

// Replaces the root layout when the layout itself fails, so it renders its own <html> and <body> and
// pulls in the global styles and tokens directly.
import "./globals.css";

export default function GlobalError({ retry }: { error: unknown; retry: () => void }) {
  return (
    <html lang="en">
      <body className="grid min-h-dvh place-items-center bg-bg p-6 font-sans text-fg antialiased">
        <div role="alert" className="max-w-md text-center">
          <h1 className="text-2xl font-semibold tracking-tight text-balance">
            The dashboard failed to load
          </h1>
          <p className="mt-2 text-pretty text-muted">
            Reload the page; if it keeps failing, the service may be down.
          </p>
          <button
            type="button"
            onClick={() => retry()}
            className="mt-6 inline-flex min-h-10 items-center rounded-md bg-primary px-4 text-sm font-medium text-primary-fg hover:bg-primary-hover"
          >
            Try again
          </button>
        </div>
      </body>
    </html>
  );
}
