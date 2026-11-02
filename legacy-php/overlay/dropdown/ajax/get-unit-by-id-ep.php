<?php
namespace Goldata;

use Goldata\CatProduct;
// GoldataWeb2 P0 hot-fix: this endpoint had no login check (NFR-SEC-2).
require_once __DIR__ . '/../../require_login.php';
\require_login();
if (! empty($_POST["product_id"])) {
    
    $productId = $_POST["product_id"];
	
    
   require_once __DIR__ . '/../Model/CatProduct.php';
   $catProduct= new CatProduct();

   $unitResult = $catProduct->getUnitByProductId($productId);

 if(isset($unitResult))
 {
    foreach ($unitResult as $unit) 
	{	
		
		 $return_arr[] = array(	"name" => $unit["table_name"],
								"item" => $unit["table_item"],
								"id" => $unit["id"],
								"data" => $unit["data"],
								"value" => $unit["value"]
								);

    }
 }else	
	{ $return_arr[] = array();}
	echo json_encode($return_arr);
}
?>