FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Plataforma.Worker/Plataforma.Worker.csproj", "src/Plataforma.Worker/"]
COPY ["src/Plataforma.Application/Plataforma.Application.csproj", "src/Plataforma.Application/"]
COPY ["src/Plataforma.Domain/Plataforma.Domain.csproj", "src/Plataforma.Domain/"]
COPY ["src/Plataforma.Infrastructure/Plataforma.Infrastructure.csproj", "src/Plataforma.Infrastructure/"]
RUN dotnet restore "src/Plataforma.Worker/Plataforma.Worker.csproj"

COPY src/ ./src/
WORKDIR /src/src/Plataforma.Worker
RUN dotnet publish "Plataforma.Worker.csproj" --configuration Release --output /app/publish --no-restore

# Plataforma.Infrastructure requiere el shared framework Microsoft.AspNetCore.App.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Plataforma.Worker.dll"]
