# syntax=docker/dockerfile:1
#
# The dashboard as Next.js's standalone server. Build context is frontend/.
# The runtime image is distroless: no shell or package manager, and it runs as a non-root user.

FROM node:25-bookworm-slim AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1 \
    NEXT_OUTPUT=standalone
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci
COPY . .
RUN npm run build

FROM gcr.io/distroless/nodejs22-debian12:nonroot AS runtime
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
# The standalone output carries its own minimal node_modules; static assets are served beside it.
# There is no public/ directory yet; copy it here once the dashboard has public assets.
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
EXPOSE 3000
CMD ["server.js"]
