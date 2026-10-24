FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Plataforma.Backoffice/Plataforma.Backoffice.csproj", "src/Plataforma.Backoffice/"]
COPY ["src/Plataforma.Application/Plataforma.Application.csproj", "src/Plataforma.Application/"]
COPY ["src/Plataforma.Infrastructure/Plataforma.Infrastructure.csproj", "src/Plataforma.Infrastructure/"]
COPY ["src/Plataforma.Domain/Plataforma.Domain.csproj", "src/Plataforma.Domain/"]
COPY ["src/Plataforma.Avisos/Plataforma.Avisos.csproj", "src/Plataforma.Avisos/"]
RUN dotnet restore "src/Plataforma.Backoffice/Plataforma.Backoffice.csproj"

COPY src/ ./src/
WORKDIR /src/src/Plataforma.Backoffice
RUN dotnet publish "Plataforma.Backoffice.csproj" --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Plataforma.Backoffice.dll"]