# Multi-stage build - image finale légère

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render fournit le port via la variable d'environnement PORT (lue dans Program.cs).
# Valeur par défaut si non fournie (ex: test local du conteneur).
ENV PORT=8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
EXPOSE 8080

ENTRYPOINT ["dotnet", "SebnWeb.dll"]
