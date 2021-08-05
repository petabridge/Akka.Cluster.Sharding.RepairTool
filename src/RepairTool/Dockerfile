FROM mcr.microsoft.com/dotnet/sdk:5.0 AS base
WORKDIR /app

# 9777 - Petabridge.Cmd
EXPOSE 9777

# Install Petabridge.Cmd client so it can be invoked remotely via
# Docker or K8s 'exec` commands
RUN dotnet tool install --global pbm 

# RUN pbm help

COPY ./bin/Release/net5/publish/ /app

FROM mcr.microsoft.com/dotnet/runtime:5.0 AS app
WORKDIR /app

COPY --from=base /app /app

# copy .NET Core global tool
COPY --from=base /root/.dotnet /root/.dotnet/

# Needed because https://stackoverflow.com/questions/51977474/install-dotnet-core-tool-dockerfile
ENV PATH="${PATH}:/root/.dotnet/tools"

CMD ["dotnet", "RepairTool.dll"]