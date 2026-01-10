# --------------------------
# Build
# --------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj first (better layer caching)
COPY NewsApplication.Web/NewsApplication.Web.csproj NewsApplication.Web/
RUN dotnet restore NewsApplication.Web/NewsApplication.Web.csproj

# Copy everything else
COPY . .
RUN dotnet publish NewsApplication.Web/NewsApplication.Web.csproj -c Release -o /app/publish \
    -p:TypeScriptCompileBlocked=true \
    -p:TypeScriptCompileOnBuild=false


# --------------------------
# Runtime
# --------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "NewsApplication.Web.dll"]
