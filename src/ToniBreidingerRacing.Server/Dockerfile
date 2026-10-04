# Build from the repository root:
# docker build -f src/ToniBreidingerRacing.Server/Dockerfile -t toni-racing-server .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/ToniBreidingerRacing.Core/ src/ToniBreidingerRacing.Core/
COPY src/ToniBreidingerRacing.Server/ src/ToniBreidingerRacing.Server/
RUN dotnet publish src/ToniBreidingerRacing.Server/ToniBreidingerRacing.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
# Required at deployment: AllowedOrigins__0=https://your-browser-app.example
ENTRYPOINT ["dotnet", "ToniBreidingerRacing.Server.dll"]
