using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using littlebreadloaf.Data;
using System.Drawing;

namespace littlebreadloaf.Pages.Products
{
    [Authorize]
    public class ProductImageAddModel : PageModel
    {
        private readonly ProductContext _context;
        
        public ProductImageAddModel(ProductContext context)
        {
            _context = context;
        }
        
        [BindProperty]
        public IFormFile FileUpload { get; set; }

        [BindProperty]
        public string ProductID { get; set; }

        [BindProperty]
        public string ProductName { get; set; }

        [BindProperty]
        public ProductImage ProductImage { get; set; }

        public async Task<IActionResult> OnGetAsync(string productID)
        {
            if (String.IsNullOrEmpty(productID) || !Guid.TryParse(productID, out Guid parsedID))
            {
                return new RedirectToPageResult("/Products/ProductList");
            }

            var product = await _context.Product.FirstOrDefaultAsync(m => m.ProductID == parsedID);

            if (product == null)
            {
                return new RedirectToPageResult("/Products/ProductList");
            }

            ProductID = product.ProductID.ToString();
            ProductName = product.Name;

            return Page();
        }

        public async Task<IActionResult> OnPostUploadAsync(string productID)
        {
            if (String.IsNullOrEmpty(productID) || !Guid.TryParse(productID, out Guid parsedID))
            {
                return new RedirectToPageResult("/Products/ProductBadgeList");
            }

            var product = await _context.Product.FirstOrDefaultAsync(m => m.ProductID == parsedID);

            if (product == null)
            {
                return new RedirectToPageResult("/Products/ProductList");
            }

            ProductName = product.Name;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var productImageID = Guid.NewGuid();
            var imgHelper = new ImageHelper(product.ProductID.ToString());
            var sizes = new List<Size>
            {
                new Size(50, 50),
                new Size(100, 100),
                new Size(200, 200),
                new Size(350, 350),
                new Size(500, 500)
            };

            try
            {
                imgHelper.AddImages(ImageHelper.ImageResizeMode.Square,
                                    productImageID.ToString(), 
                                    sizes.ToArray(), 
                                    FileUpload);
            }
            catch (Exception ex)
            {
                var systemError = new SystemError
                {
                    ErrorID = Guid.NewGuid(),
                    RequestID = product.ProductID.ToString(),
                    Path = "ProductImageAddModel",
                    Error = ex.ToString(),
                    Occurred = DateTime.Now
                };
                _context.SystemError.Add(systemError);
                await _context.SaveChangesAsync();

                ModelState.AddModelError("BadFile", "The file is invalid. It must be an image");
                return Page();
            }

            bool primary = !await _context.ProductImage.AnyAsync(a => a.ProductID == product.ProductID && a.PrimaryImage == true);
            
            ProductImage.ProductImageID = productImageID;
            ProductImage.ProductID = product.ProductID;
            ProductImage.PrimaryImage = primary;
            ProductImage.FileLocation = imgHelper.GetDisplayFileName(productImageID.ToString());

            _context.ProductImage.Add(ProductImage);
            await _context.SaveChangesAsync();

            return new RedirectToPageResult("/Products/ProductImageList", new { productID });
        }
    }
}