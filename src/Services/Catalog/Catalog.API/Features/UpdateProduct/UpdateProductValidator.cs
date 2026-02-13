// src/Services/Catalog/Catalog.API/Features/UpdateProduct/UpdateProductValidator.cs

using FluentValidation;

namespace Catalog.API.Features.UpdateProduct;

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Product ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required.")
            .MaximumLength(150).WithMessage("Product name must not exceed 150 characters.");

        RuleFor(x => x.Categories)
            .NotEmpty().WithMessage("At least one category is required.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.");

        RuleFor(x => x.ImageFile)
            .NotEmpty().WithMessage("Image file is required.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than zero.");
    }
}