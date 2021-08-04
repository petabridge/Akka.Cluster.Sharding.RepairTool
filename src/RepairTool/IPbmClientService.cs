using Akka.Actor;
using Petabridge.Cmd.Host;

namespace Petabridge.App
{
    /// <summary>
    /// Used to retrieve access to <see cref="PetabridgeCmd"/> associated with the current
    /// <see cref="ActorSystem"/>.
    /// </summary>
    public interface IPbmClientService
    {
        PetabridgeCmd Cmd { get; }
        
        ActorSystem Sys { get; }
    }
}