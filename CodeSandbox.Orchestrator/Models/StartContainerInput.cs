namespace CodeSandbox.Orchestrator.Models
{
    public class StartContainerInput
    {
        public string SasUrl { get; set; }
        public string ContainerGroupName { get; set; }
        public string ContainerName { get; set; }
    }
}