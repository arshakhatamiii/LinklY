FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for efficient caching
COPY ["UrlShortener.slnx", "./"]
COPY ["src/UrlShortener.Domain/UrlShortener.Domain.csproj", "src/UrlShortener.Domain/"]
COPY ["src/UrlShortener.Infrastructure/UrlShortener.Infrastructure.csproj", "src/UrlShortener.Infrastructure/"]
COPY ["src/UrlShortener.Api/UrlShortener.Api.csproj", "src/UrlShortener.Api/"]

RUN dotnet restore "src/UrlShortener.Api/UrlShortener.Api.csproj"

# Copy remaining source code and publish
COPY src/ src/
WORKDIR "/src/src/UrlShortener.Api"
RUN dotnet publish "UrlShortener.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "UrlShortener.Api.dll"]
