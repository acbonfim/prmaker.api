namespace solvace.knowledge.domain.Entities;

public class DomainException(string message) : Exception(message);

public class KnowledgeNotFoundException(string message) : Exception(message);
