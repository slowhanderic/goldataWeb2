<?php
namespace Goldata;

use Goldata\CatProduct;
// GoldataWeb2 P0 hot-fix: this endpoint had no login check (NFR-SEC-2).
require_once __DIR__ . '/../../require_login.php';
\require_login();
if (! empty($_POST["cat_id"])) {
    
    $catId = $_POST["cat_id"];
	$item_name = $_POST["item_name"];
    
   require_once __DIR__ . '/../Model/CatProduct.php';
   $catProduct= new CatProduct();

   $productResult = $catProduct->getProductByCatId($catId);

 if(isset($productResult))
 {
    foreach ($productResult as $product) 
	{	
		
		 $return_arr[] = array(	"name" => $product["table_name"],
								"item" => $product["table_item"],
								"id" => $product["id"],
								"data" => $product["data"],
								"value" => $product["value"]
								);

    }
 }else	
	{ $return_arr[] = array();}
	echo json_encode($return_arr);
}
?>