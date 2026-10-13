FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Plataforma.PortalComercio/Plataforma.PortalComercio.csproj", "src/Plataforma.PortalComercio/"]
COPY ["src/Plataforma.Application/Plataforma.Application.csproj", "src/Plataforma.Application/"]
COPY ["src/Plataforma.Infrastructure/Plataforma.Infrastructure.csproj", "src/Plataforma.Infrastructure/"]
COPY ["src/Plataforma.Domain/Plataforma.Domain.csproj", "src/Plataforma.Domain/"]
RUN dotnet restore "src/Plataforma.PortalComercio/Plataforma.PortalComercio.csproj"

COPY src/ ./src/
WORKDIR /src/src/Plataforma.PortalComercio
RUN dotnet publish "Plataforma.PortalComercio.csproj" --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Plataforma.PortalComercio.dll"]