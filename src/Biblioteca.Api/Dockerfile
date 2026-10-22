FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/Biblioteca.Api/Biblioteca.Api.csproj src/Biblioteca.Api/
RUN dotnet restore src/Biblioteca.Api/Biblioteca.Api.csproj
COPY src/Biblioteca.Api/ src/Biblioteca.Api/
RUN dotnet publish src/Biblioteca.Api/Biblioteca.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir /keys && chown $APP_UID:$APP_UID /keys
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Biblioteca.Api.dll"]
