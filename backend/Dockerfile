# syntax=docker/dockerfile:1
#
# One build, two images: `--target api` and `--target worker`. Build context is backend/.
# Runtime images are Ubuntu "chiseled": no shell, no package manager, non-root by default.
# The -extra variant keeps ICU and time zone data, which SQL Server client culture handling needs.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt ./
COPY src/ src/
# Restore exactly what the lock files pin; a drifted dependency fails the build here.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/PaymentPlatform.Api/PaymentPlatform.Api.csproj --locked-mode \
 && dotnet restore src/PaymentPlatform.Worker/PaymentPlatform.Worker.csproj --locked-mode

FROM build AS publish-api
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/PaymentPlatform.Api/PaymentPlatform.Api.csproj -c Release --no-restore -o /out

FROM build AS publish-worker
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/PaymentPlatform.Worker/PaymentPlatform.Worker.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

FROM runtime AS api
COPY --from=publish-api /out .
ENTRYPOINT ["dotnet", "PaymentPlatform.Api.dll"]

FROM runtime AS worker
COPY --from=publish-worker /out .
ENTRYPOINT ["dotnet", "PaymentPlatform.Worker.dll"]
