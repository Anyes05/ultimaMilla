FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Plataforma.Worker/Plataforma.Worker.csproj", "src/Plataforma.Worker/"]
COPY ["src/Plataforma.Avisos/Plataforma.Avisos.csproj", "src/Plataforma.Avisos/"]
COPY ["src/Plataforma.Observabilidad/Plataforma.Observabilidad.csproj", "src/Plataforma.Observabilidad/"]
RUN dotnet restore "src/Plataforma.Worker/Plataforma.Worker.csproj"

COPY src/ ./src/
WORKDIR /src/src/Plataforma.Worker
RUN dotnet publish "Plataforma.Worker.csproj" --configuration Release --output /app/publish --no-restore

# Sin HTTP entrante: el worker solo consume la cola, así que alcanza con el runtime base.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Plataforma.Worker.dll"]
