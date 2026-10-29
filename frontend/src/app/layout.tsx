import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { connection } from "next/server";
import { SiteHeader } from "@/components/site-header";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: { default: "Payment Platform", template: "%s | Payment Platform" },
  description: "Merchant dashboard for payments: search, statuses, and each payment's history.",
};

// Every page renders per request (ADR 009): the Content-Security-Policy's nonce is made per request, and a
// prerendered shell's scripts would not carry it, so the browser would refuse them. Every page after
// sign-in shows one merchant's data anyway, which a shared shell could not hold.
export const instant = false;

export default async function RootLayout({ children }: LayoutProps<"/">) {
  await connection();
  return (
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col font-sans">
        <a
          href="#main"
          className="sr-only rounded-md bg-primary text-sm font-medium text-primary-fg focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-20 focus:px-4 focus:py-2"
        >
          Skip to content
        </a>
        <SiteHeader />
        <main id="main" className="flex-1">
          {children}
        </main>
      </body>
    </html>
  );
}
