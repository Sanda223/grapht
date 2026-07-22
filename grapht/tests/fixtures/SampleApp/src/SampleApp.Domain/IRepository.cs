namespace SampleApp.Domain;

public interface IRepository<T>
{
    T? Find(int id);
    void Save(T entity);
}
