namespace cliqx.auth.api.Dtos
{
    // Atualização da foto (URL do Cloudinary) do próprio usuário autenticado.
    public class UpdatePhotoDto
    {
        public string ImageUrl { get; set; }
    }
}
