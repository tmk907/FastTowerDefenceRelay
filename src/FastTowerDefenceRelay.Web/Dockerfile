# Packages an app that CI has already published to publish/, see .github/workflows/vps.yml.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ ./
USER app
# The web page and API, and the relay protocol for game clients.
EXPOSE 8080 40000
ENTRYPOINT ["dotnet", "FastTowerDefenceRelay.Web.dll"]
