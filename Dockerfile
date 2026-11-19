FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-alpine3.24 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props Hearth.slnx ./
COPY src/ src/
RUN dotnet restore src/Hearth.Api/Hearth.Api.csproj
RUN dotnet publish src/Hearth.Api/Hearth.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-alpine3.24 AS runtime
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
USER root
RUN mkdir -p /data/uploads /data/keys && chown -R 1654:1654 /data
USER 1654
ENV ASPNETCORE_HTTP_PORTS=8080 App__DataPath=/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hearth.Api.dll"]
