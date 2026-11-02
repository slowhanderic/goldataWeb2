<?php
namespace Goldata;

use Goldata\SupplierName;
// GoldataWeb2 P0 hot-fix: this endpoint had no login check (NFR-SEC-2).
require_once __DIR__ . '/../../require_login.php';
\require_login();
if (! empty($_POST["supplier_name"])) {
    
    $supplier_name = $_POST["supplier_name"];
	
    
   require_once __DIR__ . '/../Model/Supplier.php';
   $supplierName= new SupplierName();

   $supplierResult = $supplierName->getSuppliersByName($supplier_name);

 if(isset($supplierResult))
 {
    foreach ($supplierResult as $supplier) 
	{	
		
		 $return_arr[] = array(	"name" => $supplier["supplier_name"],
								"id" => $supplier["id"]
								
								);

    }
 }else	
	{ $return_arr[] = array();}
	echo json_encode($return_arr);
}
?>