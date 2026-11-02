    <?php //this file was updated 20200202 hk
	setlocale(LC_ALL, 'zh_CN.UTF8');  //fix basename issue
	
	function get_current_url()
	{
		$current_url='http://';
		if(isset($_SERVER['HTTPS'])&&$_SERVER['HTTPS']=='on'){
			$current_url='https://';
		}
		if($_SERVER['SERVER_PORT']!='80'){
			//$current_url.=$_SERVER['SERVER_NAME'].':'.$_SERVER['SERVER_PORT'].$_SERVER['REQUEST_URI'];
			$current_url.=$_SERVER['HTTP_HOST'].$_SERVER['REQUEST_URI'];
		}else{
			//$current_url.=$_SERVER['SERVER_NAME'].$_SERVER['REQUEST_URI'];
			$current_url.=$_SERVER['HTTP_HOST'].$_SERVER['REQUEST_URI'];
		}
		return $current_url;
}
	function check_page_permission($page_name)
	{
		if($page_name=="payroll-edit"
			or $page_name=="payrolls-view"
			or $page_name=="payroll_items-view"
			or $page_name=="employees-view"
			or $page_name=="employee-edit"
			or $page_name=="report-salaries")
			{
				if( ($_SESSION['role']  == "管理" || $_SESSION['role']  == "财务")&& $_SESSION['rank']  == "总监" )
				{
					return true;  //employee and payroll pages can only view by 管理及财务总监
				}
				else
				{
					echo "权限不足查阅本页";
					exit();
					return false;
				}
			}
			else if($page_name=="delete_row"
					or $page_name=="lookup_tables" )
			{
				if( ($_SESSION['role']  == "管理" )&& $_SESSION['rank']  == "总监" )
				{
					return true; 
				}
				else
				{
					echo "权限不足查阅本页";
					exit();
					return false;
				}
			}
			else
			{
				return true; //other pages are free to view by all users
			}
	}
	
	
	// GoldataWeb2 (REQ-CMP-8): users come from goldata_app — see app_auth.php. The hard-coded accounts were removed.
	require_once __DIR__ . '/app_auth.php';

	function check_timeout($caller_url) //20200206
	{	$timeout = 9600; // Number of seconds until it times out.
		//	$timeout = 1200;

// Check if the timeout field exists.
		if(isset($_SESSION['timeout'])) 
		{
    		// See if the number of seconds since the last
    		// visit is larger than the timeout period.
    		$duration = time() - (int)$_SESSION['timeout'];
    		if($duration > $timeout) 
			{
				// Destroy the session and restart it.
				session_destroy();
				session_start();
				echo "<script>
						alert('登入超时!\\n 现转到登入页面');
					</script>";	
				//header('Refresh: 1; URL = index.php?expired=1');
				// echo get_current_url();
					header('location: index.php?expired='.$caller_url.'.php');
				return false;
			}
			else
			{
				return true;
			}
		}
		return false;
	}
	function check_session()
	{
		check_timeout(get_current_url());
		app_auth_revalidate();   // GoldataWeb2: disabled/reset users are signed out, role changes apply
		
		if(isset($_SESSION['username']) && $_SESSION['username']!='')
		{
			//echo '<p><img src="/web_images/goldata_logo.png" width="100" height="35" align="top"/>';
			//echo ' 登入用户：' . $_SESSION['username'] .'  ';
			//echo '<b><font color="#663300"><a href="admin-index.php">| -待办事项- |</a></font></b>';
			echo '	<nav class="navbar navbar-expand-sm  bg-dark navbar-dark fixed-top ">';
			//echo '	<nav class="navbar navbar-expand-sm   ">';
			echo '
			<div class="container-fluid">
		  
			<a class="navbar-brand" href="#">
				<img src="/web_images/goldata_logo.png" class="rounded-4"  alt="Goldata Logo" style="width:100px;" > 
			  </a>';
			  echo '  <span class="navbar-text"> 登入用户：' . $_SESSION['username'] .'  </span>';
			  echo app_auth_banner();   // GoldataWeb2: change-password reminder
			  echo  '
			 
		  
			  <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#collapsibleNavbar">
				<span class="navbar-toggler-icon"></span>
			  </button>
		  
			  <div class="collapse navbar-collapse" id="collapsibleNavbar">
				<ul class="navbar-nav">
				<li class="nav-item ">
				<a class="nav-link " href="admin-index.php" target="_blank"">待办事项</a>
			</li>
		   	';

			   if ($_SESSION['role'] == '管理' 
			  		|| $_SESSION['role'] == '财务' 
			  		|| $_SESSION['role'] == '业务'
			   		||$_SESSION['role'] == '采购'
			   		||$_SESSION['role'] == '工程文员' 
					   ||$_SESSION['role'] == '预算' ){ 
			  echo '
				<li class="nav-item dropdown">
				<a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">工装</a>
				<ul class="dropdown-menu">
					<li><a class="dropdown-item" href="projects-view.php"  target="_blank">项目</a></li>
					<li><a class="dropdown-item" href="budgets-view.php"  target="_blank">预算</a></li>
					<li><a class="dropdown-item" href="contracts-view.php"  target="_blank">合同</a></li>
					<li><a class="dropdown-item" href="receivable_applications-view.php"  target="_blank">请款</a></li>
					<li><a class="dropdown-item" href="additionals-view.php"  target="_blank">签证</a></li>
					<li><a class="dropdown-item" href="documents-view.php"  target="_blank">工程文件</a></li>
					<li><a class="dropdown-item" href="settlements-view.php"  target="_blank">结算</a></li>
				</ul>
				</li>
			 ';
			  }

			  if ($_SESSION['role'] == '管理' 
			  || $_SESSION['role'] == '财务' 
			  || $_SESSION['role'] == '业务'
			   ||$_SESSION['role'] == '采购'
			   ||$_SESSION['role'] == '工程文员'
			   ||$_SESSION['role'] == '家装' 
			   ||$_SESSION['role'] == '预算'){ 
			 echo '
				<li class="nav-item dropdown">
				<a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">家装</a>
				<ul class="dropdown-menu">
					<li><a class="dropdown-item" href="projects_retail-view.php"  target="_blank">项目</a></li>
				</ul>
				</li>
				';
			   }

			   if ($_SESSION['role'] == '管理' 
					|| $_SESSION['role'] == '财务' 
			   		|| $_SESSION['role'] == '业务'
					||$_SESSION['role'] == '采购'
					||$_SESSION['role'] == '工程文员'
					||$_SESSION['role'] == '家装'
					||$_SESSION['role'] == '预算' ){ 
			 echo '
				<li class="nav-item dropdown">
				<a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">采购</a>
				<ul class="dropdown-menu">
					<li><a class="dropdown-item" href="suppliers-view.php"  target="_blank">供应商名录</a></li>
					<div class="dropdown-divider"></div>
					<li><a class="dropdown-item" href="buy_plans-view.php"  target="_blank">采购计划</a></li>
					<li><a class="dropdown-item" href="buy_request-view.php"  target="_blank">请购单</a></li>
					<div class="dropdown-divider"></div>
					<li><a class="dropdown-item" href="buy_orders-view.php"  target="_blank">订单</a></li>
					<div class="dropdown-divider"></div>
					<li><a class="dropdown-item" href="buy_receipts-view.php"  target="_blank">收货单</a></li>
					<div class="dropdown-divider"></div>
					<li><a class="dropdown-item" href="inventory_items-view.php"  target="_blank">库存</a></li>
					<li><a class="dropdown-item" href="deliveries-view.php"  target="_blank">出库单</a></li>
					<div class="dropdown-divider"></div>
					<li><a class="dropdown-item" href="cat_product-view.php"  target="_blank">产品分类表</a></li>
					
				</ul>
				</li>
			 ';
			}

			if ($_SESSION['role'] == '管理' 
				|| $_SESSION['role'] == '财务' 	){ 
			 echo '
			 <li class="nav-item dropdown">
			   <a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">财务</a>
			   <ul class="dropdown-menu">
				 <li><a class="dropdown-item" href="sales_invoices-view.php"  target="_blank">销项发票</a></li>
				 <li><a class="dropdown-item" href="buy_invoices-view.php"  target="_blank">进项发票</a></li>
				 <div class="dropdown-divider"></div>
				 <li><a class="dropdown-item" href="receiveds-view.php"  target="_blank">收款</a></li>
				 <li><a class="dropdown-item" href="payments-view.php"  target="_blank">付款</a></li>
				 <div class="dropdown-divider"></div>
				 <li><a class="dropdown-item" href="report-index-invoices.php"  target="_blank">财务分析报告</a></li>
			   </ul>
			 </li>
			 ';
			 echo '
			 <li class="nav-item dropdown">
			   <a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">劳务</a>
			   <ul class="dropdown-menu">
				 <li><a class="dropdown-item" href="labor_settlements-view.php"  target="_blank">人工费结算单</a></li>';
				// <div class="dropdown-divider"></div>
				// <li><a class="dropdown-item"  href="labor_settlement_items-view.php" target="_blank">人工费结算单明细汇总表</a></li>
			  echo ' </ul>
			 </li>
			 ';
			}


			if (($_SESSION['role'] == '管理' 
				|| $_SESSION['role'] == '财务') 
				&& $_SESSION['rank'] == '总监' 	){ 
			 echo '
			 <li class="nav-item dropdown">
			   <a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">人事</a>
			   <ul class="dropdown-menu">
				 <li><a class="dropdown-item" href="employees-view.php"  target="_blank">员工档案</a></li>
				 <li><a class="dropdown-item" href="payrolls-view.php"  target="_blank">工资发放</a></li>
				 <div class="dropdown-divider"></div>
				 <li><a class="dropdown-item"  href="payroll_items-view.php"  target="_blank">工资发放明细汇总表</a></li>
				
				</ul>
			 </li>
			 ';
			}
			if ($_SESSION['role'] == '管理' 
					|| $_SESSION['role'] == '财务' 
			   		|| $_SESSION['role'] == '业务'
					||$_SESSION['role'] == '采购'
					||$_SESSION['role'] == '工程文员'
					||$_SESSION['role'] == '家装'
					||$_SESSION['role'] == '预算' ){ 
			 echo '
				<li class="nav-item dropdown">
				<a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">工具</a>
				<ul class="dropdown-menu">
					<li><a class="dropdown-item" href="labor_cost_estimate-edit.php"  target="_blank">安装成本计算器</a></li>
					<li><a class="dropdown-item" href="readme.php"  target="_blank">系统说明</a></li>
					</ul>
				</li>
			 ';
		}
			if ($_SESSION['username'] == '叶仲霖' )//全权
			{
			 echo '
				<li class="nav-item dropdown">
				<a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">设置</a>
				<ul class="dropdown-menu">
					<li><a class="dropdown-item" href="lookup_tables.php"  target="_blank">lookup_tables</a></li>
					</ul>
				</li>
			 ';
			}
			/*
			if ($_SESSION['role'] == '管理' || $_SESSION['role'] == '财务' || $_SESSION['role'] == '业务' )//全权
			{
				echo '<b><font color="#663300"><a href="projects-view.php"  target="_blank"> --【工装项目】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="budgets-view.php"  target="_blank"> --【预算】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="contracts-view.php"  target="_blank"> --【合同】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="additionals-view.php"  target="_blank"> --【签证】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="settlements-view.php"  target="_blank"> --【结算】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="receivable_applications-view.php"  target="_blank"> --【请款】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="documents-view.php"  target="_blank"> -【工程文件】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="sales_invoices-view.php"  target="_blank"> --【销项发票】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_invoices-view.php"  target="_blank"> --【进项发票】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="receiveds-view.php"  target="_blank"> --【收款】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="payments-view.php"  target="_blank"> --【付款】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="report-index-invoices.php"  target="_blank"> --【财务分析报告】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="projects_retail-view.php"  target="_blank"> -【家装项目】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="suppliers-view.php"  target="_blank"> -【供应商名录】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_plans-view.php"  target="_blank"> -【采购计划】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_request-view.php"  target="_blank"> -【请购单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_orders-view.php"  target="_blank"> -【采购订单/合同】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_receipts-view.php"  target="_blank"> -【收货单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="inventory_items-view.php"  target="_blank"> -【库存】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="deliveries-view.php"  target="_blank"> -【出库单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="labor_settlements-view.php"  target="_blank"> -【人工费结算单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="labor_cost_estimate-edit.php"  target="_blank"> -【安装成本计算器】-  | </a></font></b>';
				if ($_SESSION['rank'] == '总监' && ($_SESSION['role'] == '管理' || $_SESSION['role'] == '财务' ))//全权
				{	echo '<b><font color="#663300"><a href="employees-view.php"  target="_blank"> -【员工档案】-  | </a></font></b>';
					echo '<b><font color="#663300"><a href="payrolls-view.php"  target="_blank"> -【工资发放】-  | </a></font></b>';
				}
				if ($_SESSION['username'] == '叶仲霖' )//全权
				{	echo '<b><font color="#663300"><a href="lookup_tables.php"  target="_blank"> -【lookup_tables】-  | </a></font></b>';
					
				}
			}
			else if ($_SESSION['role'] == '采购')//采购
			{
				echo '<b><font color="#663300"><a href="suppliers-view.php"  target="_blank">| -【供应商名录】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="projects-view.php"  target="_blank"> --【工装项目】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_plans-view.php"  target="_blank"> -【采购计划】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_request-view.php"  target="_blank"> -【请购单】-  | </a></font></b>';
			
				echo '<b><font color="#663300"><a href="buy_orders-view.php"  target="_blank"> -【采购订单/合同】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_receipts-view.php"  target="_blank"> -【收货单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="inventory_items-view.php"  target="_blank"> -【库存】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="deliveries-view.php"  target="_blank"> -【出库单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_invoices-view.php"  target="_blank"> --【进项发票】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="report-index-invoices.php"  target="_blank"> --【财务分析报告】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="documents-view.php"  target="_blank"> -【工程文件】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="projects_retail-view.php"  target="_blank"> -【家装项目】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="labor_cost_estimate-edit.php"  target="_blank"> -【安装成本计算器】-  | </a></font></b>';
					echo '<b><font color="#663300"><a href="contracts-view.php"  target="_blank"> --【合同】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="receivable_applications-view.php"  target="_blank"> --【请款】--| </a></font></b>';
			}
			else if ($_SESSION['role'] == '采购查阅')//采购查阅
			{
				echo '<b><font color="#663300"><a href="suppliers-view.php"  target="_blank"> -【供应商名录】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_plans-view.php"  target="_blank"> -【采购计划】-  | </a></font></b>';
				
			}
			else if ($_SESSION['role'] == '工程查阅')//工程查阅
			{
					echo '<b><font color="#663300"><a href="buy_orders-view.php"  target="_blank"> -【采购订单/合同】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="documents-view.php"  target="_blank"> -【工程文件】-  | </a></font></b>';
			
				
			}
			else if ($_SESSION['role'] == '预算查阅')//预算查阅
			{
				echo '<b><font color="#663300"><a href="projects-view.php"  target="_blank"> |--【工装项目】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="budgets-view.php"  target="_blank"> --【预算】--| </a></font></b>';
				
				echo '<b><font color="#663300"><a href="contracts-view.php"  target="_blank"> --【合同】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="additionals-view.php"  target="_blank"> --【签证】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="settlements-view.php"  target="_blank"> --【结算】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="receivable_applications-view.php"  target="_blank"> --【请款】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="documents-view.php"  target="_blank"> -【工程文件】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="suppliers-view.php"  target="_blank"> -【供应商名录】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="labor_cost_estimate-edit.php"  target="_blank"> -【安装成本计算器】-  | </a></font></b>';
				
			}
			else if ($_SESSION['role'] == '家装')//家装
			{
					 
				echo '<b><font color="#663300"><a href="projects_retail-view.php"  target="_blank"> -【家装项目】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="suppliers-view.php"  target="_blank"> -【供应商名录】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_orders-view.php"  target="_blank"> -【采购订单/合同】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="labor_settlements-view.php"  target="_blank"> -【人工费结算单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_receipts-view.php"  target="_blank"> -【收货单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="inventory_items-view.php"  target="_blank"> -【库存】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="deliveries-view.php"  target="_blank"> -【出库单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="labor_cost_estimate-edit.php"  target="_blank"> -【安装成本计算器】-  | </a></font></b>';
			}
			else if ($_SESSION['role'] == '家装录入')//家装
			{
					 
				echo '<b><font color="#663300"><a href="projects_retail-view.php"  target="_blank"> -【家装项目】-  | </a></font></b>';
				
			}
			else if ($_SESSION['role'] == '预算')//预结算员
			{
					 
				echo '<b><font color="#663300"><a href="projects-view.php"  target="_blank"> |--【工装项目】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="budgets-view.php"  target="_blank"> --【预算】--| </a></font></b>';
				
				echo '<b><font color="#663300"><a href="contracts-view.php"  target="_blank"> --【合同】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="additionals-view.php"  target="_blank"> --【签证】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="settlements-view.php"  target="_blank"> --【结算】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="receivable_applications-view.php"  target="_blank"> --【请款】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="documents-view.php"  target="_blank"> -【工程文件】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="suppliers-view.php"  target="_blank"> -【供应商名录】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="labor_cost_estimate-edit.php"  target="_blank"> -【安装成本计算器】-  | </a></font></b>';
				
			}
			else if ($_SESSION['role'] == '工程文员')//工程文员
			{ 
				echo '<b><font color="#663300"><a href="projects-view.php"  target="_blank"> |--【工装项目】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_plans-view.php"  target="_blank"> -【采购计划】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_orders-view.php"  target="_blank"> -【采购订单/合同】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="buy_receipts-view.php"  target="_blank"> -【收货单】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="contracts-view.php"  target="_blank"> --【合同】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="additionals-view.php"  target="_blank"> --【签证】--| </a></font></b>';
				echo '<b><font color="#663300"><a href="documents-view.php"  target="_blank"> -【工程文件】-  | </a></font></b>';
				echo '<b><font color="#663300"><a href="projects_retail-view.php"  target="_blank"> -【家装项目】-  | </a></font></b>';
				
			} */
			
			//echo '<b><font color="#663300"><a href="readme.php"  target="_blank"> -系统说明- |</a></font></b>';
			//echo '<b><font color="#663300"><a href = "logout.php" tite = "登出"  target="_blank"> -登出- |</a></font></p>';

			echo '
					<li class="nav-item ">
						<a class="nav-link " href=" logout.php" target="_blank"">登出</a>
					</li>
		


					</ul>
					</div>
				</div>
				</nav>
		 ';
		}else
		{
			header('location: index.php?expired='.base64_encode(get_current_url()));
			// header('Refresh: 1; URL = index.php');
		}
	}
	?>