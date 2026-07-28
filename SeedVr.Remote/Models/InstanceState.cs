namespace SeedVr.Remote.Models
{
    /// <summary>Why an instance can or cannot take the job. Everything but Faulted clears on its own.</summary>
    public enum InstanceState
    {
        Unknown,

        /// <summary>Ready and free to take the job.</summary>
        Available,

        /// <summary>Ready, but already working on something.</summary>
        Busy,

        /// <summary>Still coming up: no published port, ComfyUI not answering yet, or models still downloading.</summary>
        Provisioning,

        /// <summary>Answered, but with something that needs attention rather than time.</summary>
        Faulted
    }
}
