FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/Biblioteca.Web/Biblioteca.Web.csproj src/Biblioteca.Web/
RUN dotnet restore src/Biblioteca.Web/Biblioteca.Web.csproj
COPY src/Biblioteca.Web/ src/Biblioteca.Web/
RUN dotnet publish src/Biblioteca.Web/Biblioteca.Web.csproj -c Release -o /app --no-restore /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir /keys && chown $APP_UID:$APP_UID /keys
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Biblioteca.Web.dll"]
