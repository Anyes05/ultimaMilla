FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Plataforma.PortalComercio/Plataforma.PortalComercio.csproj", "src/Plataforma.PortalComercio/"]
COPY ["src/Plataforma.Application/Plataforma.Application.csproj", "src/Plataforma.Application/"]
COPY ["src/Plataforma.Infrastructure/Plataforma.Infrastructure.csproj", "src/Plataforma.Infrastructure/"]
COPY ["src/Plataforma.Domain/Plataforma.Domain.csproj", "src/Plataforma.Domain/"]
COPY ["src/Plataforma.Avisos/Plataforma.Avisos.csproj", "src/Plataforma.Avisos/"]
COPY ["src/Plataforma.Observabilidad/Plataforma.Observabilidad.csproj", "src/Plataforma.Observabilidad/"]
RUN dotnet restore "src/Plataforma.PortalComercio/Plataforma.PortalComercio.csproj"

COPY src/ ./src/
WORKDIR /src/src/Plataforma.PortalComercio
# Sin --no-restore: en .NET 10 blazor.web.js llega como paquete de recursos estáticos y solo se incluye si
# publish restaura; con --no-restore la imagen quedaba sin el script (404 en /_framework/blazor.web.js).
RUN dotnet publish "Plataforma.PortalComercio.csproj" --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Plataforma.PortalComercio.dll"]