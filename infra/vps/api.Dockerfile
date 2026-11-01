FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY apps/api/src/ apps/api/src/
RUN dotnet restore apps/api/src/Academy.Api/Academy.Api.csproj
RUN dotnet publish apps/api/src/Academy.Api/Academy.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /out/ ./
RUN mkdir -p /data/media /data/keys && chown -R app:app /data && chmod 700 /data/keys
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Academy.Api.dll"]
